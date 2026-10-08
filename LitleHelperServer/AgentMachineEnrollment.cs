using Microsoft.EntityFrameworkCore;
using Novell.Directory.Ldap;
using System.Security.Cryptography;
namespace LitleHelperServer;
public interface IAgentMachineDirectory { Task<(string Machine,string ObjectId)> VerifyAsync(string principal,CancellationToken token); }
public sealed class AgentMachineDirectory(IntegrationSettings settings):IAgentMachineDirectory
{
    public async Task<(string Machine,string ObjectId)> VerifyAsync(string principal,CancellationToken token)
    {
        var ad=settings.Ad();var reader=settings.Telegram();if(!ad.Enabled)throw new UnauthorizedAccessException();
        if(string.IsNullOrWhiteSpace(reader.DirectoryLogin)||string.IsNullOrWhiteSpace(reader.DirectoryPassword))throw new InvalidOperationException("Настройте учётную запись чтения AD.");
        string account=principal;int at=account.IndexOf('@');if(at>=0){if(!account[(at+1)..].Equals(ad.Domain,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException();account=account[..at];}
        int slash=account.IndexOf('\\');if(slash>=0){if(!account[..slash].Equals(ad.NetbiosDomain,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException();account=account[(slash+1)..];}
        if(!account.EndsWith('$'))throw new UnauthorizedAccessException("Восстановление ключа требует учётную запись компьютера AD.");
        string machine=Security.Canonical(account[..^1]);if(!Security.MachineValid(machine))throw new UnauthorizedAccessException();
        using var ldap=AdAuthentication.Connection(ad);using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await ldap.ConnectAsync(ad.Host,ad.Port,timeout.Token);string login=reader.DirectoryLogin.IndexOfAny(['@','=','\\'])>=0?reader.DirectoryLogin:reader.DirectoryLogin+"@"+ad.Domain;
        await ldap.BindAsync(login,reader.DirectoryPassword,timeout.Token);var constraints=ldap.SearchConstraints;constraints.ReferralFollowing=false;ldap.Constraints=constraints;
        var rows=await ldap.SearchAsync(ad.BaseDn,LdapConnection.ScopeSub,$"(&(objectClass=computer)(sAMAccountName={machine}$))",["objectGUID","userAccountControl"],false,timeout.Token);LdapEntry? entry=null;
        while(await rows.HasMoreAsync(timeout.Token)){LdapEntry next;try{next=await rows.NextAsync(timeout.Token);}catch(LdapReferralException){continue;}if(entry!=null)throw new UnauthorizedAccessException();entry=next;}
        if(entry==null||!int.TryParse(entry.Get("userAccountControl")?.StringValue,out int flags)||(flags&2)!=0)throw new UnauthorizedAccessException();var bytes=entry.GetBytesValueOrDefault("objectGUID",[])??[];if(bytes.Length!=16)throw new UnauthorizedAccessException();
        return(machine,new Guid(bytes.Select(b=>unchecked((byte)b)).ToArray()).ToString("N"));
    }
}
public static class AgentMachineEnrollment
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/agents/machine-enroll",async (AgentRegistrationRequest request,HttpContext context,IMessengerKerberos kerberos,IAgentMachineDirectory directory,HelperDb db,ChatGroupGate gate,CancellationToken token)=>
        {
            context.Response.Headers.CacheControl="no-store";if(!context.Request.IsHttps||string.IsNullOrWhiteSpace(request.MachineName)||string.IsNullOrWhiteSpace(request.ClientKey))return Results.BadRequest();
            string auth=context.Request.Headers.Authorization.ToString();IResult Challenge(){context.Response.Headers.WWWAuthenticate="Negotiate";return Results.Unauthorized();}
            if(!auth.StartsWith("Negotiate ",StringComparison.OrdinalIgnoreCase)||auth.Length>65536)return Challenge();
            KerberosIdentity identity;try{identity=kerberos.Authenticate(auth[10..]);}catch(Exception ex)when(ex is System.Security.Authentication.AuthenticationException or FormatException or ArgumentException){return Challenge();}
            var verified=await directory.VerifyAsync(identity.Name,token);
            if(verified.Machine!=Security.Canonical(request.MachineName)||request.ClientKey.Length is <32 or >200||request.ClientKey.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not('+' or '/' or '=' or '-' or '_')))return Results.BadRequest();
            await gate.Semaphore.WaitAsync(token);try{
                var computer=await db.Computers.SingleOrDefaultAsync(c=>c.MachineName==verified.Machine,token);
                if(computer!=null&&computer.AdMachineObjectId.Length>0&&computer.AdMachineObjectId!=verified.ObjectId)return Results.Conflict(new{error="Учётная запись компьютера в AD была заменена. Требуется подтверждение администратора."});
                if(computer==null){computer=new(){MachineName=verified.Machine};db.Computers.Add(computer);}
                computer.AdMachineObjectId=verified.ObjectId;computer.AgentKeyHash=Security.KeyHash(request.ClientKey);await db.SaveChangesAsync(token);
                if(!string.IsNullOrEmpty(identity.ResponseToken))context.Response.Headers.WWWAuthenticate="Negotiate "+identity.ResponseToken;
                return Results.Ok(new{registered=true});
            }finally{gate.Semaphore.Release();}
        }).RequireRateLimiting("registration");
    }
}
