using Microsoft.Win32;
using PixelHelper.Updates;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;

namespace PixelHelper;

internal sealed class UpdateService : ServiceBase
{
    internal const string Name = "PixelHelperUpdater";
    private CancellationTokenSource? stop;
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PixelHelperUpdates");
    internal UpdateService() { ServiceName = Name; CanStop = true; AutoLog = false; }
    protected override void OnStart(string[] args)
    {
        stop = new(); _ = Task.Run(() => Loop(stop.Token));
    }
    protected override void OnStop() => stop?.Cancel();
    private static async Task Loop(CancellationToken token)
    {
        try { SecureFolder(); } catch { return; }
        try { await Task.Delay(TimeSpan.FromSeconds(30), token); } catch (OperationCanceledException) { return; }
        while (!token.IsCancellationRequested)
        {
            try { await CheckAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex) { Log("Проверка обновления не выполнена: " + ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromHours(1), token); } catch (OperationCanceledException) { return; }
        }
    }
    private static void SecureFolder()
    {
        Directory.CreateDirectory(Folder);
        if ((File.GetAttributes(Folder) & FileAttributes.ReparsePoint) != 0) throw new IOException("Invalid update directory");
        var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null); var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.SetOwner(system);
        foreach (var sid in new[] { system, administrators }) security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(Folder).SetAccessControl(security);
    }
    internal static async Task CheckAsync(CancellationToken token)
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.OpenSubKey(@"Software\PixelHelper");
        string? server = key?.GetValue("ServerUrl") as string;
        if (key?.GetValue("AutoUpdate") is int flag && flag == 0) return;
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0) return;
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(server!.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(5), MaxResponseContentBufferSize = 8192 };
        using var response = await http.GetAsync("api/client-updates/latest", token);
        if (response.StatusCode == HttpStatusCode.NoContent) return;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 8192) throw new InvalidDataException("Oversized manifest");
        string json = await response.Content.ReadAsStringAsync(token); if (json.Length > 8192) throw new InvalidDataException("Oversized manifest");
        var manifest = JsonSerializer.Deserialize<ClientUpdateManifest>(json, Settings.Json);
        if (manifest?.Valid() != true) throw new InvalidDataException("Invalid update signature");
        var current = typeof(UpdateService).Assembly.GetName().Version!;
        if (Version.Parse(manifest.Version) <= new Version(current.Major, current.Minor, current.Build)) return;
        SecureFolder();
        string package = Path.Combine(Folder, manifest.Sha256 + ".msi");
        using var download = await http.GetAsync("api/client-updates/package/" + manifest.Sha256, HttpCompletionOption.ResponseHeadersRead, token);
        download.EnsureSuccessStatusCode();
        if (download.Content.Headers.ContentLength != manifest.Size) throw new InvalidDataException("Unexpected update size");
        string temporary = Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".download");
        try
        {
            await using (var input = await download.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[65536]; long bytes = 0; int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                { bytes += count; if (bytes > manifest.Size) throw new InvalidDataException("Oversized update"); await output.WriteAsync(buffer.AsMemory(0, count), token); }
                if (bytes != manifest.Size) throw new InvalidDataException("Truncated update");
            }
            await using (var file = File.OpenRead(temporary))
            {
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token)).ToLowerInvariant();
                if (hash != manifest.Sha256) throw new InvalidDataException("Update hash mismatch");
            }
            File.Move(temporary, package, true);
            using (var writable = machine.OpenSubKey(@"Software\PixelHelper", true))
            {
                writable!.SetValue("UpdateRequested", manifest.Version);
                writable.SetValue("UpdateUntil", DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(), RegistryValueKind.QWord);
            }
            // Desktop processes save settings, close and launch their user-side restart watcher.
            await Task.Delay(TimeSpan.FromSeconds(35), token);
            Log("Установка версии " + manifest.Version);
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe")) { UseShellExecute = false, CreateNoWindow = true };
            foreach (string arg in new[] { "/i", package, "/qn", "/norestart", "REBOOT=ReallySuppress", "MSIRESTARTMANAGERCONTROL=Disable", "SERVERURL=" + server, "/L*v", Path.Combine(Folder, "install.log") }) start.ArgumentList.Add(arg);
            // MSI stops this service and starts the new version. Do not block OnStop.
            using var installer = Process.Start(start)!; await installer.WaitForExitAsync(token);
            Log("MSI завершён, код " + installer.ExitCode);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void Log(string message)
    {
        try { string path = Path.Combine(Folder, "update.log"); if (File.Exists(path) && new FileInfo(path).Length > 256000) File.Delete(path); File.AppendAllText(path, $"{DateTimeOffset.Now:u} {message}{Environment.NewLine}"); } catch { }
    }
}
