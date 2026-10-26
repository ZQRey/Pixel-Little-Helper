using Microsoft.Win32;
using System.IO;
using System.Text.Json;

namespace PixelHelper;

public static class BrowserExtensionDeployer
{
    public const string ExtensionId = "jhmfmcbcjmhpoogeljiailchclchcccp";
    public const string HostName = "com.pixelhelper.webguard";

    public static void EnsureDeployed()
    {
        try
        {
            string baseDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
            string extensionDir = Path.Combine(baseDir, "extension");
            string manifestPath = Path.Combine(extensionDir, "com.pixelhelper.webguard.json");
            string updateXmlPath = Path.Combine(extensionDir, "update.xml");
            string exePath = Path.Combine(baseDir, "PixelHelper.exe");

            if (!File.Exists(exePath)) return;

            // Ensure manifest file exists with exact exe path
            Directory.CreateDirectory(extensionDir);
            var manifestObj = new
            {
                name = HostName,
                description = "PixelHelper Web Guard Native Host",
                path = exePath,
                type = "stdio",
                allowed_origins = new[]
                {
                    $"chrome-extension://{ExtensionId}/"
                }
            };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifestObj, new JsonSerializerOptions { WriteIndented = true }));

            // Update XML file path for local installation
            string updateXmlUrl = new Uri(updateXmlPath).AbsoluteUri;
            string forcelistEntry = $"{ExtensionId};{updateXmlUrl}";

            // Register in HKLM (preferred for policy)
            RegisterBrowserPolicy(@"SOFTWARE\Policies\Microsoft\Edge", forcelistEntry);
            RegisterBrowserPolicy(@"SOFTWARE\Policies\Google\Chrome", forcelistEntry);
            RegisterBrowserPolicy(@"SOFTWARE\Policies\YandexBrowser", forcelistEntry);

            // Register Native Messaging Host in HKLM and HKCU
            RegisterNativeHost(@"SOFTWARE\Microsoft\Edge\NativeMessagingHosts", manifestPath);
            RegisterNativeHost(@"SOFTWARE\Google\Chrome\NativeMessagingHosts", manifestPath);
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    public static void RemoveDeployment()
    {
        try
        {
            RemoveBrowserPolicy(@"SOFTWARE\Policies\Microsoft\Edge");
            RemoveBrowserPolicy(@"SOFTWARE\Policies\Google\Chrome");
            RemoveBrowserPolicy(@"SOFTWARE\Policies\YandexBrowser");
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    private static void RemoveBrowserPolicy(string policyBasePath)
    {
        try
        {
            string forcelistSubkey = $@"{policyBasePath}\ExtensionInstallForcelist";
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(forcelistSubkey, writable: true);
            if (key != null)
            {
                foreach (var v in key.GetValueNames())
                {
                    string? val = key.GetValue(v) as string;
                    if (val != null && val.StartsWith(ExtensionId, StringComparison.OrdinalIgnoreCase))
                    {
                        key.DeleteValue(v, false);
                    }
                }
            }
        }
        catch { }
    }

    private static void RegisterBrowserPolicy(string policyBasePath, string forcelistEntry)
    {
        try
        {
            string forcelistSubkey = $@"{policyBasePath}\ExtensionInstallForcelist";
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.CreateSubKey(forcelistSubkey, writable: true);
            if (key != null)
            {
                string[] values = key.GetValueNames();
                bool alreadySet = false;
                int maxIndex = 0;
                foreach (var v in values)
                {
                    if (int.TryParse(v, out int idx) && idx > maxIndex) maxIndex = idx;
                    string? val = key.GetValue(v) as string;
                    if (val != null && val.StartsWith(ExtensionId, StringComparison.OrdinalIgnoreCase))
                    {
                        alreadySet = true;
                        key.SetValue(v, forcelistEntry);
                        break;
                    }
                }
                if (!alreadySet)
                {
                    key.SetValue((maxIndex + 1).ToString(), forcelistEntry);
                }
            }
        }
        catch { }
    }

    private static void RegisterNativeHost(string hostBasePath, string manifestPath)
    {
        try
        {
            string hostSubkey = $@"{hostBasePath}\{HostName}";
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.CreateSubKey(hostSubkey, writable: true);
            key?.SetValue(null, manifestPath);
        }
        catch { }

        try
        {
            string hostSubkey = $@"{hostBasePath}\{HostName}";
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var key = baseKey.CreateSubKey(hostSubkey, writable: true);
            key?.SetValue(null, manifestPath);
        }
        catch { }
    }
}
