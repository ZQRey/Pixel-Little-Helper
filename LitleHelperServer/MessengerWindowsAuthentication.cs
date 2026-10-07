using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using Novell.Directory.Ldap;

namespace LitleHelperServer;

public record KerberosIdentity(string Name, string? ResponseToken);
public interface IMessengerKerberos { KerberosIdentity Authenticate(string token); }
public sealed class MessengerKerberos : IMessengerKerberos
{
    public KerberosIdentity Authenticate(string token)
    {
        // Each request carries a complete Kerberos AP-REQ. NTLM's connection-bound
        // multi-step exchange is deliberately unsupported behind the shared proxy.
        using var context = new NegotiateAuthentication(new NegotiateAuthenticationServerOptions
        { Package = "Negotiate", Credential = CredentialCache.DefaultNetworkCredentials });
        string? reply = context.GetOutgoingBlob(token, out var status);
        if (status != NegotiateAuthenticationStatusCode.Completed || !context.IsAuthenticated ||
            !context.Package.Equals("Kerberos", StringComparison.OrdinalIgnoreCase) || !context.IsMutuallyAuthenticated)
            throw new AuthenticationException("Kerberos authentication failed.");
        return new(context.RemoteIdentity.Name ?? throw new AuthenticationException(), reply);
    }
}
public interface IMessengerWindowsDirectory { Task<AdIdentity> FindAsync(string principal, CancellationToken token); }
public sealed class MessengerWindowsDirectory(IntegrationSettings settings) : IMessengerWindowsDirectory
{
    public async Task<AdIdentity> FindAsync(string principal, CancellationToken token)
    {
        var ad = settings.Ad(); var reader = settings.Telegram();
        if (!ad.Enabled) throw new UnauthorizedAccessException("Вход AD отключён.");
        if (reader.DirectoryLogin.Length == 0 || reader.DirectoryPassword.Length == 0)
            throw new InvalidOperationException("Настройте учётную запись чтения AD в разделе Telegram.");
        // This value comes only from the cryptographically verified Kerberos context.
        string account = AdAuthentication.Account(principal, ad);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var ldap = AdAuthentication.Connection(ad);
        try
        {
            await ldap.ConnectAsync(ad.Host, ad.Port, timeout.Token);
            string login = reader.DirectoryLogin.IndexOfAny(['@', '=', '\\']) >= 0 ? reader.DirectoryLogin : reader.DirectoryLogin + "@" + ad.Domain;
            await ldap.BindAsync(login, reader.DirectoryPassword, timeout.Token);
            var constraints = ldap.SearchConstraints; constraints.ReferralFollowing = false; ldap.Constraints = constraints;
            var results = await ldap.SearchAsync(ad.BaseDn, LdapConnection.ScopeSub,
                $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={account}))",
                ["sAMAccountName", "displayName", "userAccountControl", "objectGUID"], false, timeout.Token);
            LdapEntry? entry = null;
            while (await results.HasMoreAsync(timeout.Token))
            {
                LdapEntry next; try { next = await results.NextAsync(timeout.Token); } catch (LdapReferralException) { continue; }
                if (entry != null) throw new UnauthorizedAccessException(); entry = next;
            }
            if (entry == null || (int.Parse(entry.Get("userAccountControl").StringValue) & 2) != 0) throw new UnauthorizedAccessException();
            string actual = AdAuthentication.Account(entry.Get("sAMAccountName").StringValue, ad);
            if (actual != account) throw new UnauthorizedAccessException();
            var bytes = entry.GetBytesValueOrDefault("objectGUID", []);
            if (bytes is not { Length: 16 }) throw new UnauthorizedAccessException();
            string name = entry.GetStringValueOrDefault("displayName", actual) ?? actual;
            return new(actual + "@" + ad.Domain, name[..Math.Min(name.Length, 150)], actual, new Guid(bytes.Select(b => unchecked((byte)b)).ToArray()).ToString("N"));
        }
        catch (LdapException) { throw new InvalidOperationException("Не удалось проверить учётную запись Windows в AD."); }
    }
}
