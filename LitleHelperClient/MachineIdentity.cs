using Microsoft.Win32;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace PixelHelper;
internal static class MachineIdentity
{
    internal record Identity(string Server,string Key);
    private static readonly byte[] Entropy=Encoding.UTF8.GetBytes("PixelHelper-Machine-Key-v1");
    internal static string Scope(string server) { var uri=new UriBuilder(server);if(uri.Scheme=="http"){uri.Scheme="https";if(uri.Port==80)uri.Port=443;}return uri.Uri.AbsoluteUri.TrimEnd('/').ToLowerInvariant(); }
    internal static string? Read(string? server)
    {
        if(Settings.TestFolder!=null||string.IsNullOrWhiteSpace(server))return null;
        try{using var hive=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64);using var key=hive.OpenSubKey(@"Software\PixelHelper");if(key?.GetValue("MachineIdentity") is not byte[] data)return null;
            var identity=JsonSerializer.Deserialize<Identity>(ProtectedData.Unprotect(data,Entropy,DataProtectionScope.LocalMachine));
            return identity!=null&&identity.Server==Scope(server)&&Convert.FromBase64String(identity.Key).Length==48?identity.Key:null;
        }catch(Exception ex)when(ex is CryptographicException or JsonException or FormatException or System.Security.SecurityException or UnauthorizedAccessException){Settings.Log(new InvalidOperationException("Чтение ключа компьютера: "+ex.GetType().Name));return null;}
    }
    internal static async Task ProvisionAsync(CancellationToken token)
    {
        using var hive=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64);using var key=hive.OpenSubKey(@"Software\PixelHelper",true);string? server=key?.GetValue("ServerUrl") as string;if(key==null||!Uri.TryCreate(server,UriKind.Absolute,out var uri)||uri.Scheme is not("http" or "https")||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0)return;
        string clientKey=Read(server)??Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        using var handler=new HttpClientHandler{UseDefaultCredentials=true,AllowAutoRedirect=false};using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(20)};
        using var response=await http.PostAsJsonAsync(Scope(server!)+"/api/agents/machine-enroll",new{machineName=Environment.MachineName,clientKey},token);response.EnsureSuccessStatusCode();
        if(Read(server)==clientKey)return;
        var value=ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(new Identity(Scope(server!),clientKey)),Entropy,DataProtectionScope.LocalMachine);key.SetValue("MachineIdentity",value,RegistryValueKind.Binary);key.Flush();
    }
    internal static async Task RunAsync(CancellationToken token)
    {
        while(!token.IsCancellationRequested){try{await ProvisionAsync(token);}catch(OperationCanceledException)when(token.IsCancellationRequested){return;}catch(Exception ex){Settings.Log(new InvalidOperationException("Проверка ключа компьютера: "+ex.GetType().Name));}try{await Task.Delay(TimeSpan.FromMinutes(1),token);}catch(OperationCanceledException){return;}}
    }
}
