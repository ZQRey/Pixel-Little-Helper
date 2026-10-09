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

    public async Task<(bool Success, string Message)> ChangePasswordAsync(string rawUsername, string newPassword, CancellationToken token = default)
    {
        var (valid, policyMsg) = ValidatePasswordPolicy(newPassword);
        if (!valid)
        {
            return (false, policyMsg);
        }

        var options = settings.Ad();
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.Host))
        {
            return (false, "Интеграция с Active Directory не настроена или отключена на сервере.");
        }

        if (string.IsNullOrWhiteSpace(options.AdminUser) || string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            return (false, "В настройках сервера не указаны логин или пароль администратора домена для смены паролей пользователей.");
        }

        string account = AdAuthentication.Account(rawUsername, options);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        using var connection = AdAuthentication.Connection(options);
        try
        {
            await connection.ConnectAsync(options.Host, options.Port, timeout.Token);

            string adminBind = options.AdminUser.Contains('@') || options.AdminUser.Contains('\\')
                ? options.AdminUser
                : options.AdminUser + "@" + options.Domain;

            await connection.BindAsync(adminBind, options.AdminPassword, timeout.Token);

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
            var attribute = new LdapAttribute("unicodePwd", encodedPassword);
            var mod = new LdapModification(LdapModification.Replace, attribute);

            await connection.ModifyAsync(entry.Dn, mod, timeout.Token);

            logger.LogInformation("Password successfully changed for AD user {Account} under admin {Admin}", account, options.AdminUser);
            return (true, "Пароль успешно изменён!");
        }
        catch (LdapException ex)
        {
            logger.LogWarning(ex, "LDAP password modification failed for user {Account}: {Error}", account, ex.LdapErrorMessage);

            string err = ex.LdapErrorMessage ?? "";
            if (err.Contains("0000052D", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Пароль не удовлетворяет истории или минимальному сроку действия паролей домена (нельзя использовать недавний пароль).");
            }
            if (err.Contains("00000005", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Отказано в доступе. У учётной записи администратора домена недостаточно прав для сброса пароля.");
            }

            return (false, "Не удалось изменить пароль в домене: " + (string.IsNullOrWhiteSpace(ex.Message) ? "ошибка контроллера AD" : ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error changing AD password for {Account}", account);
            return (false, "Ошибка сервера при обращении к Active Directory: " + ex.Message);
        }
    }
}
