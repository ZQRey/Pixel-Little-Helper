using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace PixelHelper;

public sealed record InstalledBrowser(
    string Name,
    string ExecutablePath
);

public static class BrowserDetector
{
    private static readonly (string Name, string FallbackPath)[] WellKnownBrowsers =
    {
        ("Microsoft Edge", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe")),
        ("Microsoft Edge", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe")),
        ("Google Chrome", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe")),
        ("Google Chrome", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe")),
        ("Google Chrome", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe")),
        ("Яндекс Браузер", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Yandex\YandexBrowser\Application\browser.exe")),
        ("Яндекс Браузер", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Yandex\YandexBrowser\Application\browser.exe")),
        ("Mozilla Firefox", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Mozilla Firefox\firefox.exe")),
        ("Mozilla Firefox", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Mozilla Firefox\firefox.exe")),
        ("Opera", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Opera\launcher.exe")),
        ("Brave", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"BraveSoftware\Brave-Browser\Application\brave.exe"))
    };

    public static List<InstalledBrowser> GetInstalledBrowsers()
    {
        var result = new Dictionary<string, InstalledBrowser>(StringComparer.OrdinalIgnoreCase);

        // 1. Scan Registry: HKLM & HKCU Software\Clients\StartMenuInternet
        ScanRegistry(RegistryHive.LocalMachine, result);
        ScanRegistry(RegistryHive.CurrentUser, result);

        // 2. Scan well-known fallback paths
        foreach (var (name, path) in WellKnownBrowsers)
        {
            if (File.Exists(path) && !result.ContainsKey(path))
            {
                result[path] = new InstalledBrowser(GetFriendlyName(path, name), path);
            }
        }

        return result.Values.OrderBy(b => b.Name).ToList();
    }

    private static void ScanRegistry(RegistryHive hive, Dictionary<string, InstalledBrowser> result)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var clientsKey = baseKey.OpenSubKey(@"Software\Clients\StartMenuInternet");
            if (clientsKey == null) return;

            foreach (var subKeyName in clientsKey.GetSubKeyNames())
            {
                using var browserKey = clientsKey.OpenSubKey(subKeyName);
                if (browserKey == null) continue;

                using var cmdKey = browserKey.OpenSubKey(@"shell\open\command");
                if (cmdKey == null) continue;

                string? rawCmd = cmdKey.GetValue(null) as string;
                string? exePath = ExtractExePath(rawCmd);
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) continue;

                // Ignore PixelHelper itself and old Internet Explorer
                if (exePath.Contains("PixelHelper", StringComparison.OrdinalIgnoreCase) ||
                    exePath.EndsWith("iexplore.exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                string displayName = browserKey.GetValue(null) as string ?? subKeyName;
                displayName = GetFriendlyName(exePath, displayName);

                if (!result.ContainsKey(exePath))
                {
                    result[exePath] = new InstalledBrowser(displayName, exePath);
                }
            }
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    public static string ExtractExePath(string? rawCmd)
    {
        if (string.IsNullOrWhiteSpace(rawCmd)) return "";
        string trimmed = rawCmd.Trim();
        if (trimmed.StartsWith("\""))
        {
            int nextQuote = trimmed.IndexOf('"', 1);
            if (nextQuote > 1) return trimmed.Substring(1, nextQuote - 1);
        }
        int spaceIndex = trimmed.IndexOf(' ');
        if (spaceIndex > 0 && !File.Exists(trimmed))
        {
            string candidate = trimmed.Substring(0, spaceIndex);
            if (File.Exists(candidate)) return candidate;
        }
        return trimmed;
    }

    private static string GetFriendlyName(string exePath, string fallback)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            if (!string.IsNullOrWhiteSpace(info.FileDescription))
            {
                string desc = info.FileDescription.Trim();
                if (!desc.Contains("exe", StringComparison.OrdinalIgnoreCase))
                    return desc;
            }
            if (!string.IsNullOrWhiteSpace(info.ProductName))
            {
                return info.ProductName.Trim();
            }
        }
        catch { }

        string fileName = Path.GetFileName(exePath).ToLowerInvariant();
        return fileName switch
        {
            "msedge.exe" => "Microsoft Edge",
            "chrome.exe" => "Google Chrome",
            "browser.exe" => "Яндекс Браузер",
            "firefox.exe" => "Mozilla Firefox",
            "launcher.exe" => "Opera",
            "brave.exe" => "Brave",
            _ => fallback
        };
    }
}
