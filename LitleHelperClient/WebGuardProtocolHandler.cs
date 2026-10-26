using System.IO;
using Microsoft.Win32;

namespace PixelHelper;

public static class WebGuardProtocolHandler
{
    private const string ProtocolProgId = "PixelHelperURL";
    private const string RegisteredApplication = @"Software\RegisteredApplications";

    /// <summary>
    /// Registers a per-user URL protocol candidate without taking over http/https.
    /// Windows 10/11 only honor the user's explicit UserChoice selection.
    /// </summary>
    public static void SetAsDefault()
    {
        try
        {
            string baseDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
            string exePath = Path.Combine(baseDir, "PixelHelper.exe");
            if (!File.Exists(exePath)) return;

            string commandValue = $"\"{exePath}\" --guard-url \"%1\"";

            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using (var progIdKey = baseKey.CreateSubKey($@"Software\Classes\{ProtocolProgId}", writable: true))
            {
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
            }

            using (var registered = baseKey.CreateSubKey(RegisteredApplication, writable: true))
                registered?.SetValue("PixelHelper", $@"Software\Classes\{ProtocolProgId}");

            // Remove overrides written by older versions; the browser is chosen by the user.
            RemoveLegacyOverrides(baseKey);
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    /// <summary>
    /// Removes PixelHelper URL overrides. The browser remains the Windows user's default.
    /// </summary>
    public static void RestoreUserDefault(string browserExePath)
    {
        _ = browserExePath;
        RemoveOverride();
    }

    /// <summary>
    /// Removes only PixelHelper-owned HKCU protocol overrides and the URL protocol candidate.
    /// </summary>
    public static void RemoveOverride()
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            RemoveLegacyOverrides(baseKey);
            baseKey.DeleteSubKeyTree($@"Software\Classes\{ProtocolProgId}", false);
            using var registered = baseKey.OpenSubKey(RegisteredApplication, writable: true);
            if (registered?.GetValue("PixelHelper") as string == $@"Software\Classes\{ProtocolProgId}")
                registered.DeleteValue("PixelHelper", false);
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    private static void RemoveLegacyOverrides(RegistryKey baseKey)
    {
        RemoveProtocolOverride(baseKey, "http");
        RemoveProtocolOverride(baseKey, "https");
    }

    private static void RemoveProtocolOverride(RegistryKey baseKey, string protocol)
    {
        string path = $@"Software\Classes\{protocol}\shell\open\command";
        using var command = baseKey.OpenSubKey(path);
        if (IsPixelHelperCommand(command?.GetValue(null) as string))
            baseKey.DeleteSubKeyTree(path, false);
    }

    private static bool IsPixelHelperCommand(string? command) =>
        !string.IsNullOrWhiteSpace(command) &&
        command.Contains("PixelHelper.exe", StringComparison.OrdinalIgnoreCase) &&
        command.Contains("--guard-url", StringComparison.OrdinalIgnoreCase);
}
