using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace PixelHelper;

public static class SelfDestructManager
{
    private const string AppName = "PixelLittleHelper";

    public static bool RemoveFromStartup()
    {
        bool success = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                if (key.GetValue(AppName) != null) key.DeleteValue(AppName, false);
                if (key.GetValue("PixelHelper") != null) key.DeleteValue("PixelHelper", false);
            }
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
            success = false;
        }

        try
        {
            string startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string link1 = Path.Combine(startupDir, "PixelLittleHelper.lnk");
            string link2 = Path.Combine(startupDir, "PixelHelper.lnk");
            if (File.Exists(link1)) File.Delete(link1);
            if (File.Exists(link2)) File.Delete(link2);
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
            success = false;
        }

        return success;
    }

    public static bool RemoveShortcuts()
    {
        bool success = true;
        try
        {
            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string programsDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);

            string[] targets =
            [
                Path.Combine(desktopDir, "PixelLittleHelper.lnk"),
                Path.Combine(desktopDir, "PixelHelper.lnk"),
                Path.Combine(programsDir, "PixelLittleHelper.lnk"),
                Path.Combine(programsDir, "PixelHelper.lnk")
            ];

            foreach (var file in targets)
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
            success = false;
        }

        return success;
    }

    public static string GenerateCleanupScript(int processId, string exePath, string appDataDir)
    {
        string dirPath = Path.GetDirectoryName(exePath) ?? exePath;
        // Wait for process termination, delete exe, delete app data
        return $"/c \"timeout /t 3 /nobreak >nul & taskkill /F /PID {processId} >nul 2>&1 & del /f /q \"{exePath}\" >nul 2>&1 & rmdir /s /q \"{appDataDir}\" >nul 2>&1\"";
    }

    public static void ExecuteSelfDestruct(bool simulateOnly = false)
    {
        try
        {
            RemoveFromStartup();
            RemoveShortcuts();

            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                exePath = Path.Combine(AppContext.BaseDirectory, "PixelHelper.exe");
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDataDir = Path.Combine(appData, AppName);
            int pid = Environment.ProcessId;

            string scriptArgs = GenerateCleanupScript(pid, exePath, appDataDir);

            if (!simulateOnly)
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = scriptArgs,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                Process.Start(startInfo);
                Environment.Exit(0);
            }
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
            if (!simulateOnly)
            {
                Environment.Exit(0);
            }
        }
    }
}
