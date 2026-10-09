using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LitleHelperServer;

public record TelegramOptions(bool Enabled = false, string BotToken = "", string ChatId = "", int ThreadId = 0,
    bool ManagementEnabled = false, string DirectoryLogin = "", string DirectoryPassword = "", string IdAttribute = "physicalDeliveryOfficeName");
public record TelegramUpdate(bool Enabled, string? BotToken, string ChatId, int ThreadId = 0, bool ClearBotToken = false,
    bool ManagementEnabled = false, string DirectoryLogin = "", string? DirectoryPassword = null, string IdAttribute = "physicalDeliveryOfficeName", bool ClearDirectoryPassword = false);
public record AdOptions(bool Enabled = false, string Host = "", int Port = 636, string Domain = "", string NetbiosDomain = "", string BaseDn = "", string CaCertificate = "");
public record GlpiImportOptions(bool Enabled = false, Dictionary<int,int>? LocationBranches = null);
public record CartridgeOptions(bool Enabled = true, string ApiKey = "");
public class SpecialistDepartmentConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SpecialistTitle { get; set; } = "";
    public string Color { get; set; } = "#2563eb";
    public List<string> ResponsibleUsers { get; set; } = new();
}
public record EmergencyOptions(
    bool Enabled = false,
    string ServerUrl = "http://172.16.16.63:8085",
    string ApiKey = "",
    bool AllowStandalone = true,
    bool AllowClientTrigger = true,
    List<string>? AllowedRoles = null,
    List<SpecialistDepartmentConfig>? Departments = null,
    List<string>? CodeBlueResponsibleUsers = null,
    bool TelegramAlertsEnabled = true,
    string TelegramChatId = "",
    int TelegramThreadId = 0
);

public class IntegrationSettings(IConfiguration configuration, IDataProtectionProvider protection)
{
    private readonly object gate = new();
    private readonly IDataProtector protector = protection.CreateProtector("LitleHelper.Integrations.v1");
    private string PathName => configuration["Integrations:SettingsFile"] ?? Path.Combine("data", "integrations.json");
    private record Stored(TelegramOptions Telegram, AdOptions Ad, GlpiImportOptions? GlpiImport = null, CartridgeOptions? Cartridge = null, EmergencyOptions? Emergency = null);
    public GlpiImportOptions GlpiImport() { lock(gate) return ReadStored().GlpiImport ?? new(); }
    public void SaveGlpiImport(GlpiImportOptions options)
    {
        if (options.LocationBranches?.Count>500 || options.LocationBranches?.Any(p=>p.Key<=0 || p.Value<=0)==true) throw new ArgumentException("Проверьте сопоставление местоположений и филиалов.");
        lock(gate) Write(ReadStored() with { GlpiImport=options });
    }
    public CartridgeOptions Cartridge() { lock(gate) return ReadStored().Cartridge ?? new(); }
    public void SaveCartridge(CartridgeOptions options) { lock(gate) Write(ReadStored() with { Cartridge = options }); }
    public EmergencyOptions Emergency() { lock(gate) return ReadStored().Emergency ?? new(); }
    public void SaveEmergency(EmergencyOptions options) { lock(gate) Write(ReadStored() with { Emergency = options }); }
    private Stored ReadStored() => File.Exists(PathName) ? JsonSerializer.Deserialize<Stored>(File.ReadAllText(PathName))! : new(new(), new());
    public TelegramOptions Telegram() { lock (gate) { var value = ReadStored().Telegram; return value with { BotToken = value.BotToken.Length == 0 ? "" : protector.Unprotect(value.BotToken), DirectoryPassword = value.DirectoryPassword.Length == 0 ? "" : protector.Unprotect(value.DirectoryPassword) }; } }
    public object TelegramView() { var v = Telegram(); return new { v.Enabled, v.ChatId, v.ThreadId, v.ManagementEnabled, v.DirectoryLogin, v.IdAttribute, hasBotToken = v.BotToken.Length > 0, hasDirectoryPassword = v.DirectoryPassword.Length > 0 }; }
    public AdOptions Ad() { lock (gate) return ReadStored().Ad; }
    private void Write(Stored value)
    {
        string path = Path.GetFullPath(PathName); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value)); File.Move(path + ".tmp", path, true);
    }
    public void SaveTelegram(TelegramUpdate update)
    {
        if (update.DirectoryLogin.Length > 256 || update.DirectoryPassword?.Length > 256 || !Regex.IsMatch(update.IdAttribute, @"^[A-Za-z][A-Za-z0-9-]{0,63}$") ||
            update.ThreadId < 0 || update.ChatId.Length > 100 || update.BotToken?.Length > 300 ||
            (update.ChatId.Length > 0 && !Regex.IsMatch(update.ChatId, @"^-?\d+$|^@[A-Za-z0-9_]+$")) ||
            (!string.IsNullOrEmpty(update.BotToken) && !Regex.IsMatch(update.BotToken, @"^\d+:[A-Za-z0-9_-]+$")))
            throw new ArgumentException("Проверьте токен, Chat ID и ID темы Telegram.");
        lock (gate)
        {
            var old = ReadStored(); var token = update.ClearBotToken ? "" : string.IsNullOrEmpty(update.BotToken) ? old.Telegram.BotToken : protector.Protect(update.BotToken);
            if (update.Enabled && (token.Length == 0 || update.ChatId.Length == 0)) throw new ArgumentException("Для включения Telegram нужны токен бота и Chat ID.");
            string password = update.ClearDirectoryPassword ? "" : string.IsNullOrEmpty(update.DirectoryPassword) ? old.Telegram.DirectoryPassword : protector.Protect(update.DirectoryPassword);
            if (update.ManagementEnabled && (token.Length == 0 || password.Length == 0 || string.IsNullOrWhiteSpace(update.DirectoryLogin)))
                throw new ArgumentException("Для управления заявками нужны токен бота и учётная запись чтения AD.");
            Write(old with { Telegram = new(update.Enabled, token, update.ChatId, update.ThreadId, update.ManagementEnabled, update.DirectoryLogin.Trim(), password, update.IdAttribute) });
        }
    }
    public void SaveAd(AdOptions update)
    {
        if (update.Port is < 1 or > 65535 || update.Host.Length > 253 || update.Domain.Length > 150 || update.NetbiosDomain.Length > 30 || update.BaseDn.Length > 1000 || update.CaCertificate.Length > 30000 ||
            (update.Host.Length > 0 && Uri.CheckHostName(update.Host) == UriHostNameType.Unknown) ||
            (update.Domain.Length > 0 && !Regex.IsMatch(update.Domain, @"^[a-zA-Z0-9.-]+$")) ||
            (update.NetbiosDomain.Length > 0 && !Regex.IsMatch(update.NetbiosDomain, @"^[a-zA-Z0-9_-]+$")) || update.BaseDn.Contains('\0'))
            throw new ArgumentException("Проверьте адрес LDAPS, домен и Base DN.");
        if (update.Enabled && (update.Host.Length == 0 || update.Domain.Length == 0 || update.BaseDn.Length == 0)) throw new ArgumentException("Для AD нужны контроллер домена, домен и Base DN.");
        if (update.CaCertificate.Length > 0)
        {
            try { var roots = new X509Certificate2Collection(); roots.ImportFromPem(update.CaCertificate); if (roots.Count == 0) throw new ArgumentException(); }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or ArgumentException) { throw new ArgumentException("Укажите сертификат центра сертификации в PEM-формате."); }
        }
        lock (gate) Write(ReadStored() with { Ad = update with { Host = update.Host.Trim(), Domain = update.Domain.Trim().ToLowerInvariant(), NetbiosDomain = update.NetbiosDomain.Trim() } });
    }
}
