using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace PixelHelper;

// Invoked only by the elevated, deferred MSI uninstall action, never on normal startup.
internal static class MachineCleanup
{
    internal static int Run()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            if (!principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator) && !identity.IsSystem) return 5;
            string installedExe = Environment.ProcessPath!;
            foreach (var process in Process.GetProcessesByName("PixelHelper"))
            {
                using (process)
                {
                    if (process.Id == Environment.ProcessId) continue;
                    try
                    {
                        if (!string.Equals(process.MainModule?.FileName, installedExe, StringComparison.OrdinalIgnoreCase)) continue;
                        process.CloseMainWindow();
                        if (!process.WaitForExit(3000)) { process.Kill(); process.WaitForExit(3000); }
                    }
                    catch (InvalidOperationException) { }
                }
            }
            using var profiles = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
            foreach (string sid in profiles?.GetSubKeyNames() ?? [])
            {
                if (!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal) && !sid.StartsWith("S-1-12-1-", StringComparison.Ordinal)) continue;
                using var profile = profiles!.OpenSubKey(sid);
                string home = Environment.ExpandEnvironmentVariables(profile?.GetValue("ProfileImagePath") as string ?? "");
                if (!Path.IsPathFullyQualified(home) || !Directory.Exists(home)) continue;
                string mount = sid;
                bool loadedByUs = false;
                using (var existing = Registry.Users.OpenSubKey(sid))
                {
                    if (existing == null)
                    {
                        string hive = Path.Combine(home, "NTUSER.DAT");
                        if (!File.Exists(hive)) continue;
                        mount = "PixelHelperCleanup_" + Guid.NewGuid().ToString("N");
                        if (Reg("load", "HKU\\" + mount, hive) != 0) return 10;
                        loadedByUs = true;
                    }
                }
                try
                {
                    using var user = Registry.Users.OpenSubKey(mount, true) ?? throw new IOException("Cannot open user hive");
                    using (var run = user.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) run?.DeleteValue("PixelHelper", false);
                    user.DeleteSubKeyTree(@"Software\PixelHelper", false);
                    using var folders = user.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
                    string appData = folders?.GetValue("AppData", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? Path.Combine(home, "AppData", "Roaming");
                    appData = appData.Replace("%USERPROFILE%", home, StringComparison.OrdinalIgnoreCase);
                    // Never expand another user's HOME/APPDATA using the MSI service's environment.
                    if (!Path.IsPathFullyQualified(appData) || appData.Contains('%')) return 11;
                    string data = Path.GetFullPath(Path.Combine(appData, "PixelHelper"));
                    if (!string.Equals(Path.GetFileName(data), "PixelHelper", StringComparison.OrdinalIgnoreCase)) return 12;
                    if (Directory.Exists(data)) Directory.Delete(data, true);
                }
                finally
                {
                    if (loadedByUs && Reg("unload", "HKU\\" + mount) != 0) throw new IOException("Cannot unload user hive");
                }
            }
            return 0;
        }
        catch { return 1; }
    }
    private static int Reg(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "reg.exe"))
        { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.WaitForExit(); return process.ExitCode;
    }
}
