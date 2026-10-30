using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace PixelHelper;

/// <summary>
/// Manages corporate OEM branding in System Properties and Windows Lock Screen / Logon Screen.
/// Applied by MSI installer, PixelHelperUpdater Windows Service (LocalSystem), or via CLI --apply-branding.
/// </summary>
public static class BrandingManager
{
    public const string DefaultManufacturer = "ReyTech";
    public const string DefaultModel = "PixelHelper";
    public const string DefaultSupportHours = "Пн-Пт 08:00 – 17:00";
    public const string DefaultSupportUrl = "https://helper.gp1.loc";

    private const string OemKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\OEMInformation";
    private const string PersonalizationPolicyKeyPath = @"SOFTWARE\Policies\Microsoft\Windows\Personalization";
    private const string PersonalizationCspKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP";
    private const string SystemPolicyKeyPath = @"SOFTWARE\Policies\Microsoft\Windows\System";
    private const string Win7LogonKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\Background";

    public static string GetAssetsDirectory()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string assetsDir = Path.Combine(baseDir, "Assets");
        if (Directory.Exists(assetsDir) && File.Exists(Path.Combine(assetsDir, "logo_oem.bmp")))
            return assetsDir;

        // Fallback search up the directory tree (for dev / test environments)
        string? current = baseDir;
        for (int i = 0; i < 6 && current != null; i++)
        {
            string candidate = Path.Combine(current, "Assets");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "logo_oem.bmp")))
                return candidate;
            current = Path.GetDirectoryName(current);
        }

        return assetsDir;
    }

    public static string GetLogoPath() => Path.Combine(GetAssetsDirectory(), "logo_oem.bmp");
    public static string GetLockScreenPath() => Path.Combine(GetAssetsDirectory(), "branding_lockscreen.jpg");

    /// <summary>
    /// Applies OEM Information and Lock Screen policy to HKLM.
    /// Requires elevated / SYSTEM permissions. Returns false safely if running without write access.
    /// </summary>
    public static bool ApplyBranding(string? serverUrl = null, Action<string>? log = null)
    {
        try
        {
            string url = !string.IsNullOrWhiteSpace(serverUrl) ? serverUrl.Trim() : DefaultSupportUrl;
            string logoPath = GetLogoPath();
            string lockScreenPath = GetLockScreenPath();

            // 1. OEM Information
            using (var oemKey = Registry.LocalMachine.CreateSubKey(OemKeyPath, true))
            {
                if (oemKey != null)
                {
                    oemKey.SetValue("Manufacturer", DefaultManufacturer, RegistryValueKind.String);
                    oemKey.SetValue("Model", DefaultModel, RegistryValueKind.String);
                    oemKey.SetValue("SupportHours", DefaultSupportHours, RegistryValueKind.String);
                    oemKey.SetValue("Часы", DefaultSupportHours, RegistryValueKind.String);
                    try { oemKey.DeleteValue("SupportPhone", false); } catch { }
                    try { oemKey.DeleteValue("Телефон", false); } catch { }
                    oemKey.SetValue("SupportURL", url, RegistryValueKind.String);

                    if (File.Exists(logoPath))
                    {
                        oemKey.SetValue("Logo", logoPath, RegistryValueKind.String);
                    }
                    log?.Invoke($"OEM Information applied: Manufacturer={DefaultManufacturer}, Model={DefaultModel}");
                }
            }

            // 2. Windows 10/11 Lock Screen Policy & PersonalizationCSP (Pro, Enterprise, Home)
            if (File.Exists(lockScreenPath))
            {
                try
                {
                    using var policyKey = Registry.LocalMachine.CreateSubKey(PersonalizationPolicyKeyPath, true);
                    if (policyKey != null)
                    {
                        policyKey.SetValue("LockScreenImage", lockScreenPath, RegistryValueKind.String);
                        policyKey.SetValue("NoChangingLockScreen", 1, RegistryValueKind.DWord);
                        log?.Invoke($"Lock screen policy applied: {lockScreenPath}");
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke($"Personalization policy notice: {ex.Message}");
                }

                try
                {
                    using var cspKey = Registry.LocalMachine.CreateSubKey(PersonalizationCspKeyPath, true);
                    if (cspKey != null)
                    {
                        cspKey.SetValue("LockScreenImagePath", lockScreenPath, RegistryValueKind.String);
                        cspKey.SetValue("LockScreenImageUrl", lockScreenPath, RegistryValueKind.String);
                        cspKey.SetValue("LockScreenImageStatus", 1, RegistryValueKind.DWord);
                        log?.Invoke($"PersonalizationCSP applied: {lockScreenPath}");
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke($"PersonalizationCSP notice: {ex.Message}");
                }

                try
                {
                    using var sysKey = Registry.LocalMachine.CreateSubKey(SystemPolicyKeyPath, true);
                    if (sysKey != null)
                    {
                        sysKey.SetValue("DisableLogonBackgroundImage", 0, RegistryValueKind.DWord);
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke($"System policy notice: {ex.Message}");
                }

                // Also update Windows default lock screen image img100.jpg if accessible
                try
                {
                    string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    string screenDir = Path.Combine(winDir, "Web", "Screen");
                    string defaultImg = Path.Combine(screenDir, "img100.jpg");
                    if (File.Exists(defaultImg))
                    {
                        string backupImg = Path.Combine(screenDir, "img100.original.bak");
                        if (!File.Exists(backupImg))
                        {
                            try { File.Copy(defaultImg, backupImg, false); } catch { }
                        }
                        File.Copy(lockScreenPath, defaultImg, true);
                        log?.Invoke($"Windows default screen image updated: {defaultImg}");
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke($"Default screen file replace notice: {ex.Message}");
                }
            }

            // 3. Windows 7 Logon Background Support
            try
            {
                using var logonKey = Registry.LocalMachine.CreateSubKey(Win7LogonKeyPath, true);
                logonKey?.SetValue("OEMBackground", 1, RegistryValueKind.DWord);

                // Windows 7 OEM background folder
                string win7Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "oobe", "info", "backgrounds");
                if (File.Exists(lockScreenPath))
                {
                    var fi = new FileInfo(lockScreenPath);
                    if (fi.Length <= 256 * 1024) // Windows 7 limit is 256 KB
                    {
                        Directory.CreateDirectory(win7Dir);
                        string dest = Path.Combine(win7Dir, "backgroundDefault.jpg");
                        File.Copy(lockScreenPath, dest, overwrite: true);
                        log?.Invoke($"Windows 7 OEM background copied: {dest}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Non-critical Win7 path error
                log?.Invoke($"Win7 logon background notice: {ex.Message}");
            }

            return true;
        }
        catch (SecurityException ex)
        {
            log?.Invoke($"Branding skipped (insufficient rights): {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            log?.Invoke($"Branding skipped (unauthorized): {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Branding error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reads current branding state from HKLM.
    /// </summary>
    public static (string? Manufacturer, string? Model, string? SupportUrl, string? LockScreenImage) GetCurrentBranding()
    {
        string? mfg = null;
        string? model = null;
        string? url = null;
        string? lockScreen = null;

        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(OemKeyPath, false))
            {
                if (key != null)
                {
                    mfg = key.GetValue("Manufacturer") as string;
                    model = key.GetValue("Model") as string;
                    url = key.GetValue("SupportURL") as string;
                }
            }

            using (var key = Registry.LocalMachine.OpenSubKey(PersonalizationPolicyKeyPath, false))
            {
                if (key != null)
                {
                    lockScreen = key.GetValue("LockScreenImage") as string;
                }
            }

            if (string.IsNullOrEmpty(lockScreen))
            {
                using var key = Registry.LocalMachine.OpenSubKey(PersonalizationCspKeyPath, false);
                if (key != null)
                {
                    lockScreen = key.GetValue("LockScreenImagePath") as string;
                }
            }
        }
        catch
        {
            // Ignore read errors
        }

        return (mfg, model, url, lockScreen);
    }
}
