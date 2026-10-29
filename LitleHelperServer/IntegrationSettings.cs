using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LitleHelperServer;

public record TelegramOptions(bool Enabled = false, string BotToken = "", string ChatId = "", int ThreadId = 0,
    bool ManagementEnabled = false, string DirectoryLogin = "", string DirectoryPassword = "", string IdAttribute = "physicalDeliveryOfficeName");
public record TelegramUpdate(bool Enabled, string? BotToken, string ChatId, int ThreadId = 0, bool ClearBotToken = false,
    bool ManagementEnabled = false, string DirectoryLogin = "", string? DirectoryPassword = null, string IdAttribute = "physicalDeliveryOfficeName", bool ClearDirectoryPassword = false);
public record AdOptions(bool Enabled = false, string Host = "", int Port = 636, string Domain = "", string NetbiosDomain = "", string BaseDn = "", string CaCertificate = "", string AdminUser = "", string AdminPassword = "");
public record AdUpdate(bool Enabled, string Host, int Port, string Domain, string NetbiosDomain, string BaseDn, string CaCertificate = "", string AdminUser = "", string? AdminPassword = null, bool ClearAdminPassword = false);
public record GlpiImportOptions(bool Enabled = false, Dictionary<int,int>? LocationBranches = null);
public record CartridgeOptions(bool Enabled = true, string ApiKey = "");
public class SpecialistDepartmentConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SpecialistTitle { get; set; } = "";
    public string Color { get; set; } = "#2563eb";
    public List<string> ResponsibleUsers { get; set; } = new();
    public List<string> ResponsibleGroups { get; set; } = new();
    public Dictionary<string, List<string>>? BranchResponsibleUsers { get; set; } = new();
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
    int TelegramThreadId = 0,
    List<string>? CodeBlueResponsibleGroups = null,
    bool BroadcastAllBranches = false,
    Dictionary<string, List<string>>? EmergencyCodeLanguages = null,
    List<string>? AllowedCodes = null,
    Dictionary<string,List<string>>? CodeLaunchRoles = null,
    Dictionary<string, List<string>>? CodeBlueBranchUsers = null
);

public record SecurityOptions(
    bool WebGuardEnabled = true,
    bool AntivirusEnabled = true,
    int AntivirusScheduleHours = 6,
    int AntivirusRetentionDays = 30,
    string PerformanceProfile = "auto",
    int MaxCpuPercent = 25,
    bool PauseOnUserActivity = true,
    bool ClamAvEnabled = true,
    bool YaraEnabled = true,
    bool HashCheckEnabled = true,
    string ExcludedExtensions = ".iso, .vmdk, .bak, .1cd, .log",
    string ExcludedPaths = "",
    long MaxScanSizeBytes = 262144000,
    string ThreatAction = "quarantine"
);
public record RegistrationOptions(List<string> AllowedNetworks);
public record PxeOptions(
    bool Enabled = false,
    string ServerUrl = "http://172.16.16.61:8080",
    int TimeoutSeconds = 10,
    string ApiKey = ""
);
public record PxeSettingsUpdate(
    bool Enabled,
    string ServerUrl,
    int TimeoutSeconds = 10,
    string ApiKey = ""
);

public class IntegrationSettings(IConfiguration configuration, IDataProtectionProvider protection)
{
    private readonly object gate = new();
    private readonly IDataProtector protector = protection.CreateProtector("LitleHelper.Integrations.v1");
    private string PathName => configuration["Integrations:SettingsFile"] ?? Path.Combine("data", "integrations.json");
    private record Stored(TelegramOptions Telegram, AdOptions Ad, GlpiImportOptions? GlpiImport = null, CartridgeOptions? Cartridge = null, EmergencyOptions? Emergency = null, Dictionary<int,int>? DepartmentMap = null, SecurityOptions? Security = null, RegistrationOptions? Registration = null, PxeOptions? Pxe = null);
    public SecurityOptions Security() { lock(gate) return ReadStored().Security ?? new(); }
    public void SaveSecurity(SecurityOptions options) { lock(gate) Write(ReadStored() with { Security = options }); }
    public RegistrationOptions Registration() { lock(gate) return ReadStored().Registration ?? new(NetworkAccess.DefaultRegistrationNetworks.ToList()); }
    public PxeOptions Pxe() { lock(gate) return ReadStored().Pxe ?? new(); }
    public void SavePxe(PxeSettingsUpdate update)
    {
        var url = (update.ServerUrl ?? "").Trim().TrimEnd('/');
        if (update.Enabled && string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Укажите корректный адрес PXE-сервера.");
        var options = new PxeOptions(
            Enabled: update.Enabled,
            ServerUrl: string.IsNullOrWhiteSpace(url) ? "http://172.16.16.61:8080" : url,
            TimeoutSeconds: update.TimeoutSeconds > 0 ? update.TimeoutSeconds : 10,
            ApiKey: update.ApiKey ?? ""
        );
        lock(gate) Write(ReadStored() with { Pxe = options });
    }
    public void SaveRegistration(RegistrationOptions options)
    {
        var networks = NetworkAccess.NormalizeNetworks(options.AllowedNetworks);
        lock(gate) Write(ReadStored() with { Registration = new RegistrationOptions(networks.ToList()) });
    }
    public object RegistrationView() => new
    {
        allowedNetworks = Registration().AllowedNetworks,
        defaultNetworks = NetworkAccess.DefaultRegistrationNetworks,
        allowPublic = configuration.GetValue<bool>("Registration:AllowPublic")
    };
    public GlpiImportOptions GlpiImport() { lock(gate) return ReadStored().GlpiImport ?? new(); }
    public void SaveGlpiImport(GlpiImportOptions options)
    {
        if (options.LocationBranches?.Count>500 || options.LocationBranches?.Any(p=>p.Key<=0 || p.Value<=0)==true) throw new ArgumentException("Проверьте сопоставление местоположений и филиалов.");
        lock(gate) Write(ReadStored() with { GlpiImport=options });
    }
    public CartridgeOptions Cartridge() { lock(gate) return ReadStored().Cartridge ?? new(); }
    public void SaveCartridge(CartridgeOptions options) { if(options.Enabled && string.IsNullOrWhiteSpace(options.ApiKey))throw new ArgumentException("Для включения интеграции задайте API-ключ."); lock(gate) Write(ReadStored() with { Cartridge = options }); }
    public EmergencyOptions Emergency() { lock(gate) return ReadStored().Emergency ?? new(); }
    public void SaveEmergency(EmergencyOptions options)
    {
        EmergencyLanguages.Validate(options);
        if (options.CodeBlueBranchUsers != null)
        {
            var aggregated = options.CodeBlueBranchUsers.Values.SelectMany(x => x).Distinct().ToList();
            options = options with { CodeBlueResponsibleUsers = aggregated };
        }
        if (options.Departments != null)
        {
            foreach (var dept in options.Departments)
            {
                if (dept.BranchResponsibleUsers != null && dept.BranchResponsibleUsers.Count > 0)
                {
                    dept.ResponsibleUsers = dept.BranchResponsibleUsers.Values.SelectMany(x => x).Distinct().ToList();
                }
            }
        }
        lock(gate) Write(ReadStored() with { Emergency = options });
        EmergencyDirectorySync.Request();
    }
    public void SaveDepartmentMap(Dictionary<int,int> map) {lock(gate) Write(ReadStored() with {DepartmentMap=map});}
    public int? ExternalDepartmentId(int local) {lock(gate) return ReadStored().DepartmentMap is { } map && map.TryGetValue(local,out int value)?value:null;}
    public int? LocalDepartmentId(int external) {lock(gate) {
        var matches=(ReadStored().DepartmentMap??[]).Where(p=>p.Value==external).ToArray();
        return matches.Length==1?matches[0].Key:null;
    }}
    private Stored ReadStored() => File.Exists(PathName) ? JsonSerializer.Deserialize<Stored>(File.ReadAllText(PathName))! : new(new(), new());
    public TelegramOptions Telegram() { lock (gate) { var value = ReadStored().Telegram; return value with { BotToken = value.BotToken.Length == 0 ? "" : protector.Unprotect(value.BotToken), DirectoryPassword = value.DirectoryPassword.Length == 0 ? "" : protector.Unprotect(value.DirectoryPassword) }; } }
    public object TelegramView() { var v = Telegram(); return new { v.Enabled, v.ChatId, v.ThreadId, v.ManagementEnabled, v.DirectoryLogin, v.IdAttribute, hasBotToken = v.BotToken.Length > 0, hasDirectoryPassword = v.DirectoryPassword.Length > 0 }; }
    public AdOptions Ad()
    {
        lock (gate)
        {
            var value = ReadStored().Ad;
            return value with { AdminPassword = value.AdminPassword.Length == 0 ? "" : protector.Unprotect(value.AdminPassword) };
        }
    }
    public object AdView()
    {
        var v = Ad();
        return new { v.Enabled, v.Host, v.Port, v.Domain, v.NetbiosDomain, v.BaseDn, v.CaCertificate, v.AdminUser, hasAdminPassword = v.AdminPassword.Length > 0 };
    }
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
    public void SaveAd(AdUpdate update)
    {
        if (update.Port is < 1 or > 65535 || update.Host.Length > 253 || update.Domain.Length > 150 || update.NetbiosDomain.Length > 30 || update.BaseDn.Length > 1000 || update.CaCertificate.Length > 30000 ||
            update.AdminUser.Length > 256 || update.AdminPassword?.Length > 256 ||
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
        lock (gate)
        {
            var old = ReadStored();
            string pwd = update.ClearAdminPassword ? "" : string.IsNullOrEmpty(update.AdminPassword) ? old.Ad.AdminPassword : protector.Protect(update.AdminPassword);
            var updated = new AdOptions(update.Enabled, update.Host.Trim(), update.Port, update.Domain.Trim().ToLowerInvariant(), update.NetbiosDomain.Trim(), update.BaseDn, update.CaCertificate, update.AdminUser.Trim(), pwd);
            Write(old with { Ad = updated });
        }
    }
    public void SaveAd(AdOptions update) => SaveAd(new AdUpdate(update.Enabled, update.Host, update.Port, update.Domain, update.NetbiosDomain, update.BaseDn, update.CaCertificate, update.AdminUser, update.AdminPassword));
}
