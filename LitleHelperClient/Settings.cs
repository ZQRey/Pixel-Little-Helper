using Microsoft.Win32;
using System.IO;
using System.Text.Json;

namespace PixelHelper;

public sealed class Settings
{
    public double? X { get; set; }
    public double? Y { get; set; }
    public string? ServerUrl { get; set; } = "http://helper-server:5000";
    public string? HubUrl { get; set; }
    public string ClientToken { get; set; } = "";
    public bool EnableAdministrativeCommands { get; set; } = true;
    public bool AllowRemoteCommands { get; set; }
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixelHelper");
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
        if (Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment))
            settings.ServerUrl = uri.AbsoluteUri.TrimEnd('/');
    }
    public void Save() => AtomicWrite(Path.Combine(Folder, "config.json"), JsonSerializer.Serialize(this, Json));
    public static void AtomicWrite(string path, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", value);
        File.Move(path + ".tmp", path, true);
    }
    public static bool FirstRunCompleted
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RegistryPath); return key?.GetValue("FirstRunCompleted") is int v && v == 1; }
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
