using Novell.Directory.Ldap;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace LitleHelperServer;

public record AdIdentity(string Username, string FullName, string AccountName);
public interface IAdAuthentication
{
    Task<AdIdentity> AuthenticateAsync(LoginRequest request, CancellationToken token);
    Task TestConnectionAsync(CancellationToken token);
}
public class AdAuthentication(IntegrationSettings settings, ILogger<AdAuthentication> logger) : IAdAuthentication
{
    public static string Account(string value, AdOptions options)
    {
        value = value.Trim();
        if (value.Contains('\\'))
        {
            var parts = value.Split('\\');
            if (parts.Length != 2 || !parts[0].Equals(options.NetbiosDomain, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Неверный логин или пароль AD.");
            value = parts[1];
        }
        if (value.Contains('@'))
        {
            var parts = value.Split('@');
            if (parts.Length != 2 || !parts[1].Equals(options.Domain, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Неверный логин или пароль AD.");
            value = parts[0];
        }
        if (value.Length is < 1 or > 64 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')))
            throw new UnauthorizedAccessException("Неверный логин или пароль AD.");
        return value.ToLowerInvariant();
    }
    private static LdapConnection Connection(AdOptions options)
    {
        var connectionOptions = new LdapConnectionOptions().UseSsl();
        if (options.CaCertificate.Length > 0)
        {
            connectionOptions.ConfigureRemoteCertificateValidationCallback((_, certificate, _, errors) =>
            {
                if (certificate == null || (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0) return false;
                using var chain = new X509Chain(); chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.ImportFromPem(options.CaCertificate);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.ApplicationPolicy.Add(new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1"));
                using var server = new X509Certificate2(certificate); return chain.Build(server);
            });
        }
        return new LdapConnection(connectionOptions);
    }
    public async Task TestConnectionAsync(CancellationToken token)
    {
        var options = settings.Ad();
        if (options.Host.Length == 0) throw new ArgumentException("Сначала укажите адрес контроллера AD.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var connection = Connection(options);
        try { await connection.ConnectAsync(options.Host, options.Port, timeout.Token); }
        catch (Exception ex) when (ex is not ArgumentException) { throw new InvalidOperationException("Не удалось подключиться по LDAPS. Проверьте DNS, порт и доверенный сертификат CA."); }
    }
    public async Task<AdIdentity> AuthenticateAsync(LoginRequest request, CancellationToken token)
    {
        var options = settings.Ad();
        if (!options.Enabled) throw new UnauthorizedAccessException("Вход AD отключён.");
        string account = Account(request.Username, options);
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 256) throw new UnauthorizedAccessException("Неверный логин или пароль AD.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var connection = Connection(options);
        try
        {
            await connection.ConnectAsync(options.Host, options.Port, timeout.Token);
            await connection.BindAsync(account + "@" + options.Domain, request.Password, timeout.Token);
            var results = await connection.SearchAsync(options.BaseDn, LdapConnection.ScopeSub,
                $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={account}))", ["sAMAccountName", "displayName", "userAccountControl"], false, timeout.Token);
            if (!await results.HasMoreAsync(timeout.Token)) throw new UnauthorizedAccessException("Неверный логин или пароль AD.");
            var entry = await results.NextAsync(timeout.Token);
            string actual = entry.Get("sAMAccountName").StringValue.ToLowerInvariant();
            if (!actual.Equals(account, StringComparison.OrdinalIgnoreCase) || await results.HasMoreAsync(timeout.Token)) throw new UnauthorizedAccessException("Неверный логин или пароль AD.");
            int flags = int.Parse(entry.Get("userAccountControl").StringValue);
            if ((flags & 2) != 0) throw new UnauthorizedAccessException("Неверный логин или пароль AD.");
            string fullName = entry.GetStringValueOrDefault("displayName", actual) ?? actual;
            return new(actual + "@" + options.Domain, fullName[..Math.Min(fullName.Length, 150)], actual);
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.InvalidCredentials)
        {
            var diagnostic = System.Text.RegularExpressions.Regex.Match(ex.LdapErrorMessage ?? "", @"\bdata\s+([0-9a-fA-F]{3,8})\b");
            logger.LogWarning("AD bind rejected: LDAP {Code}, AD subcode {Subcode}", ex.ResultCode, diagnostic.Success ? diagnostic.Groups[1].Value : "unavailable");
            throw new UnauthorizedAccessException("Неверный логин или пароль AD.");
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException) { throw new InvalidOperationException("AD недоступен. Проверьте LDAPS, сертификат CA и Base DN."); }
    }
}
