using Novell.Directory.Ldap;
using System.Text;
using System.Text.RegularExpressions;

namespace LitleHelperServer;

public sealed class AdPasswordService(IntegrationSettings settings, ILogger<AdPasswordService> logger)
{
    public static (bool Valid, string Message) ValidatePasswordPolicy(string password)
    {
        if (string.IsNullOrEmpty(password))
            return (false, "Пароль не может быть пустым.");
        if (password.Length < 8)
            return (false, "Пароль должен содержать не менее 8 символов.");
        if (!password.Any(char.IsUpper))
            return (false, "Пароль должен содержать хотя бы одну заглавную букву (A-Z / А-Я).");
        if (!password.Any(char.IsLower))
            return (false, "Пароль должен содержать хотя бы одну строчную букву (a-z / а-я).");
        if (!password.Any(char.IsDigit))
            return (false, "Пароль должен содержать хотя бы одну цифру (0-9).");
        return (true, "Пароль соответствует политике безопасности.");
    }

    public async Task<(bool Success, string Message)> ChangePasswordAsync(string rawUsername, string newPassword, string currentPassword, CancellationToken token = default)
    {
        var (valid, policyMsg) = ValidatePasswordPolicy(newPassword);
        if (!valid)
        {
            return (false, policyMsg);
        }

        if(string.IsNullOrWhiteSpace(currentPassword))return (false,"Введите текущий пароль.");
        var options = settings.Ad();
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.Host))
        {
            return (false, "Интеграция с Active Directory не настроена или отключена на сервере.");
        }

        if (string.IsNullOrWhiteSpace(currentPassword))
            return (false, "Введите текущий пароль.");

        string account = AdAuthentication.Account(rawUsername, options);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        using var connection = AdAuthentication.Connection(options);
        try
        {
            await connection.ConnectAsync(options.Host, options.Port, timeout.Token);

            // Bind as the account whose password is being changed; a machine key
            // alone never grants a domain password reset.
            await connection.BindAsync(account + "@" + options.Domain, currentPassword, timeout.Token);

            var constraints = connection.SearchConstraints;
            constraints.ReferralFollowing = false;
            connection.Constraints = constraints;

            var results = await connection.SearchAsync(
                options.BaseDn,
                LdapConnection.ScopeSub,
                $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={account}))",
                ["distinguishedName", "sAMAccountName"],
                false,
                timeout.Token
            );

            LdapEntry? entry = null;
            while (await results.HasMoreAsync(timeout.Token))
            {
                try { entry = await results.NextAsync(timeout.Token); break; }
                catch (LdapReferralException) { }
            }

            if (entry == null)
            {
                return (false, $"Пользователь {account} не найден в Active Directory.");
            }

            // In Active Directory, password must be quoted UTF-16LE bytes
            byte[] encodedPassword = Encoding.Unicode.GetBytes($"\"{newPassword}\"");
            var changes = new[] {
                new LdapModification(LdapModification.Delete, new LdapAttribute("unicodePwd", Encoding.Unicode.GetBytes($"\"{currentPassword}\""))),
                new LdapModification(LdapModification.Add, new LdapAttribute("unicodePwd", encodedPassword))
            };
            await connection.ModifyAsync(entry.Dn, changes, timeout.Token);
            logger.LogInformation("AD self-service password change completed for {Account}", account);
            return (true, "Пароль успешно изменён!");
        }
        catch (LdapException ex)
        {
            logger.LogWarning("LDAP password modification failed for user {Account}, code {Code}", account, ex.ResultCode);

            string err = ex.LdapErrorMessage ?? "";
            if (err.Contains("0000052D", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Пароль не удовлетворяет истории или минимальному сроку действия паролей домена (нельзя использовать недавний пароль).");
            }
            if (err.Contains("00000005", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Отказано в доступе. У учётной записи администратора домена недостаточно прав для сброса пароля.");
            }

            return (false, "Не удалось изменить пароль: проверьте текущий пароль и требования домена.");
        }
        catch (Exception ex)
        {
            logger.LogError("AD password change failed for {Account}: {Error}", account,ex.GetType().Name);
            return (false, "Не удалось связаться с контроллером домена.");
        }
    }
}
