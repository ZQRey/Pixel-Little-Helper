using System.IO;
using Microsoft.Win32;

namespace PixelHelper;

public static class WebGuardProtocolHandler
{
    private const string ProtocolProgId = "PixelHelperURL";

    /// <summary>
    /// Configures PixelHelper as default browser/URL handler in HKCU for the current user.
    /// Sets http, https, and .html handlers to PixelHelperURL.
    /// </summary>
    public static void SetAsDefault()
    {
        try
        {
            string baseDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
            string exePath = Path.Combine(baseDir, "PixelHelper.exe");
            if (!File.Exists(exePath)) return;

            string commandValue = $"\"{exePath}\" --guard-url \"%1\"";

            // 1. Ensure HKCU\Software\Classes\PixelHelperURL is registered
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            {
                using var progIdKey = baseKey.CreateSubKey($@"Software\Classes\{ProtocolProgId}", writable: true);
                if (progIdKey != null)
                {
                    progIdKey.SetValue(null, "PixelHelper Guard URL");
                    progIdKey.SetValue("FriendlyTypeName", "PixelHelper Guard URL");
                    progIdKey.SetValue("URL Protocol", "");

                    using var iconKey = progIdKey.CreateSubKey("DefaultIcon", writable: true);
                    iconKey?.SetValue(null, $"\"{exePath}\",0");

                    using var cmdKey = progIdKey.CreateSubKey(@"shell\open\command", writable: true);
                    cmdKey?.SetValue(null, commandValue);
                }

                // 2. Set per-user protocol handlers in HKCU\Software\Classes
                SetProtocolCommand(baseKey, "http", commandValue);
                SetProtocolCommand(baseKey, "https", commandValue);
            }
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    /// <summary>
    /// Restores the user's preferred browser executable as default in HKCU\Software\Classes\http and https.
    /// </summary>
    public static void RestoreUserDefault(string browserExePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(browserExePath) || !File.Exists(browserExePath))
            {
                RemoveOverride();
                return;
            }

            string browserCmd = $"\"{browserExePath}\" \"%1\"";

            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            SetProtocolCommand(baseKey, "http", browserCmd);
            SetProtocolCommand(baseKey, "https", browserCmd);

            // Also update PixelHelperURL in HKCU to point directly to browser as a fallback
            using var cmdKey = baseKey.CreateSubKey($@"Software\Classes\{ProtocolProgId}\shell\open\command", writable: true);
            cmdKey?.SetValue(null, browserCmd);
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    /// <summary>
    /// Removes HKCU\Software\Classes protocol overrides so system default ProgId is used.
    /// </summary>
    public static void RemoveOverride()
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            baseKey.DeleteSubKeyTree(@"Software\Classes\http\shell\open\command", false);
            baseKey.DeleteSubKeyTree(@"Software\Classes\https\shell\open\command", false);
            baseKey.DeleteSubKeyTree($@"Software\Classes\{ProtocolProgId}", false);
        }
        catch { }
    }

    private static void SetProtocolCommand(RegistryKey baseKey, string protocol, string command)
    {
        try
        {
            using var cmdKey = baseKey.CreateSubKey($@"Software\Classes\{protocol}\shell\open\command", writable: true);
            cmdKey?.SetValue(null, command);
        }
        catch { }
    }
}
