using Microsoft.Win32;
using System.IO;
using System.Text.Json;

namespace PixelHelper;

public sealed class Settings
{
    public double? X { get; set; }
    public double? Y { get; set; }
    public string? ServerUrl { get; set; } = "https://helper.gp1.loc";
    public string? HubUrl { get; set; }
    public string ClientToken { get; set; } = "";
    public bool EnableAdministrativeCommands { get; set; } = true;
    public bool AllowRemoteCommands { get; set; }
    public bool AllowCompanionChat { get; set; } = true;
    public int? TicketBranchId { get; set; }
    public string TicketRoom { get; set; } = "";
    public bool ChatSound { get; set; } = true;
    public string SoundProfile { get; set; } = "Sound";
    public bool ChatPreview { get; set; } = true;
    public bool ChatDoNotDisturb { get; set; }
    public bool ChatUrgentOverridesQuiet { get; set; } = true;
    public string MedicalWorkStatus { get; set; } = "Available";
    public List<MedicalStatusConfig> MedicalStatuses { get; set; } = MedicalStatusConfig.Defaults();
    [System.Text.Json.Serialization.JsonIgnore] public bool MedicalSoundsMuted => MedicalStatuses.FirstOrDefault(s => s.IsActive && s.Key == MedicalWorkStatus)?.MuteSounds == true;
    [System.Text.Json.Serialization.JsonIgnore] public bool MedicalTtsMuted => MedicalStatuses.FirstOrDefault(s => s.IsActive && s.Key == MedicalWorkStatus)?.MuteTts == true;
    public bool EnableTtsEmergencyAlerts { get; set; } = true;
    public bool EnableEyeCareReminders { get; set; } = true;
    public List<string> ChatCommandsShown { get; set; } = new();
    public List<string> UrgentNotificationsShown { get; set; } = new();
    public bool ChatWindowsNotifications { get; set; } = true;
    public bool ChatComicNotifications { get; set; } = true;
    public string DisplayMode { get; set; } = "Background";
    public string Language { get; set; } = "ru";
    public string LicenseStatus { get; set; } = "Licensed";
    public DateTime FirstStartupDate { get; set; } = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    public bool AssistantHidden { get; set; }
    public bool EmojiReactions { get; set; } = true;
    public bool IncomingEmojiReactions { get; set; } = true;
    public bool AnimatedChatEmojis { get; set; } = true;
    public string ChatTheme { get; set; } = "Helper";
    public double ChatFontSize { get; set; } = 13;
    public string ChatBackground { get; set; } = "Dots";
    public string? ChatBackgroundImage { get; set; }
    public double ChatBackgroundDim { get; set; } = .35;
    public string SelectedSkinId { get; set; } = "Classic";
    public bool WebGuardEnabled { get; set; } = true;
    public List<string> WebGuardWhitelist { get; set; } = new();
    public List<string> WebGuardBlacklist { get; set; } = new();
    public string? PreferredBrowserPath { get; set; }
    public string? PreferredBrowserName { get; set; }
    public bool BrowserPromptShown { get; set; }
    public bool AntivirusEnabled { get; set; } = true;
    public int AntivirusRetentionDays { get; set; } = 30;
    public string AntivirusPerformanceProfile { get; set; } = "auto";
    public int AntivirusMaxCpuPercent { get; set; } = 25;
    public bool AntivirusPauseOnUserActivity { get; set; } = true;
    public string AntivirusExcludedExtensions { get; set; } = ".iso, .vmdk, .bak, .1cd, .log";
    public string AntivirusExcludedPaths { get; set; } = "";
    public long AntivirusMaxScanSizeBytes { get; set; } = 262144000;
    public List<MailAccountConfig> MailAccounts { get; set; } = new();
    public bool MailNotifyWhenHidden { get; set; } = true;
    public bool MailScanAntivirus { get; set; } = true;
    public bool MailScanPhishing { get; set; } = true;
    public bool MailFilterSpam { get; set; } = true;
    public List<string> MailBlockedSenders { get; set; } = new();
    public List<string> MailTrustedSenders { get; set; } = new();
    public string MailSharedEncryptionKey { get; set; } = "";
    internal bool EnsureClientKey()
    {
        string? machineKey = MachineIdentity.Read(ServerUrl);
        if (machineKey != null) { bool changed = ClientToken != machineKey; ClientToken = machineKey; return changed; }
        if (!string.IsNullOrWhiteSpace(ClientToken)) return false;
        ClientToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
        return true;
    }
    internal static string? TestFolder;
    public static string Folder => TestFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixelHelper");
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    private const string RegistryPath = @"Software\PixelHelper";
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static Settings Load()
    {
        Settings settings;
        try
        {
            var path = Path.Combine(Folder, "config.json");
            if (!File.Exists(path))
            {
                var defaults = Path.Combine(AppContext.BaseDirectory, "config.example.json");
                settings = File.Exists(defaults) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(defaults), Json) ?? new() : new();
            }
            else settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? new();
        }
        catch (Exception ex) { Log(ex); settings = new(); }
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(RegistryPath);
            ApplyInstalledServer(settings, key?.GetValue("ServerUrl") as string);
        }
        catch (Exception ex) { Log(ex); }
        return settings;
    }
    internal static void ApplyInstalledServer(Settings settings, string? serverUrl)
    {
        if (!string.IsNullOrWhiteSpace(serverUrl) && !serverUrl.Any(c => char.IsWhiteSpace(c) || c is '\\' or ',') &&
            Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) && uri.HostNameType != UriHostNameType.Unknown &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment))
        {
            if (uri.Scheme == Uri.UriSchemeHttp && uri.Host.Equals("helper.gp1.loc", StringComparison.OrdinalIgnoreCase))
                uri = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
            settings.ServerUrl = uri.AbsoluteUri.TrimEnd('/');
        }
    }
    private static readonly object writeGate = new();
    public void Save() { lock (writeGate) AtomicWrite(Path.Combine(Folder, "config.json"), JsonSerializer.Serialize(this, Json)); }
    public static void AtomicWrite(string path, string value)
    {
        lock (writeGate)
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, value); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    public static bool FirstRunCompleted
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RegistryPath); return key?.GetValue("FirstRunCompleted") is int v && v == 1; }
    }
    internal static void PrepareStartup()
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var run = machine.OpenSubKey(RunPath);
            if (string.Equals(run?.GetValue("PixelHelper") as string, '"' + Environment.ProcessPath + '"', StringComparison.OrdinalIgnoreCase))
                SetStartup(false); // Remove the legacy per-user entry only after MSI provided machine startup.
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            key.SetValue("FirstRunCompleted", 1, RegistryValueKind.DWord);
        }
        catch (Exception ex) { Log(ex); }
    }
    public static void CompleteFirstRun(bool startup)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        SetStartup(startup);
        key.SetValue("FirstRunCompleted", 1, RegistryValueKind.DWord);
    }
    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunPath);
        if (enabled) key.SetValue("PixelHelper", '"' + Environment.ProcessPath! + '"');
        else key.DeleteValue("PixelHelper", false);
    }
    public static void ResetUserData()
    {
        SetStartup(false);
        Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, false);
        if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
    }
    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, "error.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256_000) File.Delete(path);
            File.AppendAllText(path, $"{DateTimeOffset.Now:u} [INFO] {message}{Environment.NewLine}");
        }
        catch { }
    }
    public static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, "error.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256_000) File.Delete(path);
            File.AppendAllText(path, $"{DateTimeOffset.Now:u} {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
        }
        catch { /* Logging must not interrupt desktop interaction. */ }
    }
}
