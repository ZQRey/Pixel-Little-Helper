using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;

namespace LitleHelperServer;

public sealed record AiModel(string Id, string Provider = "", string Price = "Unknown");
public sealed record AiDiagnosticTarget(string Id, string Title, string Url, string[] AllowedAddresses);
public sealed record AiOptions
{
    public bool Enabled { get; init; }
    public bool PilotOnly { get; init; } = true;
    public int[] PilotUserIds { get; init; } = [];
    public bool UseKnowledge { get; init; }
    public bool DiagnosticsEnabled { get; init; }
    public AiDiagnosticTarget[] DiagnosticTargets { get; init; } = [];
    public string Backend { get; init; } = "CloudApi";
    public string OpenCodeMode { get; init; } = "Shared";
    // Internal routing identity, set only by ForUser and preserved in encrypted cleanup snapshots.
    public int WorkerOwnerId { get; init; }
    public string BaseUrl { get; init; } = "https://openrouter.ai/api/v1/";
    public string OpenCodeUrl { get; init; } = "http://opencode:4096/";
    public string DefaultModel { get; init; } = "openrouter/free";
    public AiModel[] Models { get; init; } = [new("openrouter/free", "openrouter", "Free")];
    public int RequestsPerDay { get; init; } = 30;
    public int MaxOutputTokens { get; init; } = 1024;
    public int TimeoutSeconds { get; init; } = 90;
    public int RetentionDays { get; init; } = 30;
    public string Version { get; init; } = Guid.NewGuid().ToString("N");
    // Secrets are never returned by the API. Blank updates preserve stored values.
    public string ApiKey { get; init; } = "";
    public string OpenCodePassword { get; init; } = "";
    public bool ClearApiKey { get; init; }
    public bool ClearOpenCodePassword { get; init; }
}

public static class AiAccess
{
    public const string ChatChannel = "chat";
    public const string CompanionChannel = "companion";
    public const string ChannelHeader = "X-Helper-Ai-Channel";

    public static bool IsAdministrator(PanelUser user) => user.Role is Roles.Admin or Roles.SuperAdmin;
    public static bool PilotEligible(AiOptions options, PanelUser user) => !options.PilotOnly || IsAdministrator(user) || options.PilotUserIds.Contains(user.Id);
    public static string Channel(HttpRequest request) => string.Equals(request.Headers[ChannelHeader].ToString(), CompanionChannel, StringComparison.OrdinalIgnoreCase) ? CompanionChannel : ChatChannel;
    public static string Permission(string channel) => channel == CompanionChannel ? "ai.agent" : "ai.chat";
    public static bool CanChannel(PanelUser user, string channel) => Access.Can(user, Permission(channel));
    public static bool CanChatOrAgent(PanelUser user) => Access.Can(user, "ai.chat") || Access.Can(user, "ai.agent");
}

public sealed partial class AiSettingsStore
{
    private readonly object gate = new();
    private readonly IDataProtector protector;
    private readonly string path;
    public AiSettingsStore(IDataProtectionProvider protection, IConfiguration configuration)
    {
        protector = protection.CreateProtector("LitleHelper.AI.Settings.v1");
        path = configuration["Ai:SettingsFile"] ?? Path.Combine("data", "ai-settings.json");
    }
    public AiOptions Read(bool secrets = false)
    {
        lock (gate)
        {
            var value = File.Exists(path) ? JsonSerializer.Deserialize<AiOptions>(File.ReadAllText(path))! : new();
            return value with { ApiKey = secrets ? Decode(value.ApiKey) : "", OpenCodePassword = secrets ? Decode(value.OpenCodePassword) : "" };
        }
    }
    public object View()
    {
        lock (gate)
        {
            var value = Read(true);
            return new { options = value with { ApiKey = "", OpenCodePassword = "" }, apiKeyConfigured = value.ApiKey.Length > 0,
                openCodePasswordConfigured = value.OpenCodePassword.Length > 0, paidModelsAvailable = false,
                personalCredentialsAvailable = true, diagnosticsAvailable = true };
        }
    }
    private string Decode(string value) => value.Length == 0 ? "" : protector.Unprotect(value);
    public string Snapshot(AiOptions value) => protector.Protect(JsonSerializer.Serialize(value));
    public AiOptions RestoreSnapshot(string value)
    {
        var options = JsonSerializer.Deserialize<AiOptions>(protector.Unprotect(value))!;
        // Older personal snapshots predate lifecycle routing; immutable URL bindings identify their owner.
        if (options.Backend == "OpenCode" && options.OpenCodeMode == "Personal" && options.WorkerOwnerId == 0)
        {
            lock (gate)
            {
                var worker = Workers().SingleOrDefault(w => w.Url == options.OpenCodeUrl);
                if (worker is not null) options = options with { WorkerOwnerId = worker.OwnerUserId };
            }
        }
        return options;
    }
    public void Save(AiOptions update)
    {
        Validate(update);
        lock (gate)
        {
            var old = Read(true);
            string key = update.ClearApiKey ? "" : string.IsNullOrWhiteSpace(update.ApiKey) ? old.ApiKey : update.ApiKey;
            string password = update.ClearOpenCodePassword ? "" : string.IsNullOrWhiteSpace(update.OpenCodePassword) ? old.OpenCodePassword : update.OpenCodePassword;
            if (key.Length > 4096 || password.Length > 4096) throw new ArgumentException("AI credential is too long");
            if (update.Enabled && (update.Backend == "CloudApi" ? key.Length == 0 : update.OpenCodeMode == "Shared" && password.Length == 0))
                throw new ArgumentException("Configure credentials before enabling AI");
            if (Workers().Any(w => Endpoint(w.Url) == Endpoint(update.OpenCodeUrl))) throw new ArgumentException("Corporate OpenCode address is reserved by a personal worker");
            var saved = update with { Version = Guid.NewGuid().ToString("N"), ApiKey = key.Length == 0 ? "" : protector.Protect(key),
                OpenCodePassword = password.Length == 0 ? "" : protector.Protect(password), ClearApiKey = false, ClearOpenCodePassword = false };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(saved));
            File.Move(temp, path, true);
        }
    }
    public static void Validate(AiOptions value)
    {
        if (value.Backend is not ("CloudApi" or "OpenCode")) throw new ArgumentException("Unknown AI backend");
        if (value.OpenCodeMode is not ("Shared" or "Personal")) throw new ArgumentException("Unknown OpenCode mode");
        if (value.WorkerOwnerId != 0) throw new ArgumentException("Worker owner is assigned by the server");
        if (value.PilotUserIds is null || value.PilotUserIds.Length > 200 || value.PilotUserIds.Any(id => id < 1) || value.PilotUserIds.Distinct().Count() != value.PilotUserIds.Length)
            throw new ArgumentException("Invalid pilot user list");
        if (value.Enabled && value.PilotOnly && value.PilotUserIds.Length == 0) throw new ArgumentException("Select pilot users before enabling AI");
        ValidateUrl(value.BaseUrl, false); ValidateUrl(value.OpenCodeUrl, true);
        if (value.DiagnosticTargets is null || value.DiagnosticTargets.Length > 20 || value.DiagnosticTargets.Any(t => t is null ||
            string.IsNullOrWhiteSpace(t.Id) || t.Id.Length > 60 || t.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-')) ||
            string.IsNullOrWhiteSpace(t.Title) || t.Title.Length > 100) || value.DiagnosticTargets.Select(t => t.Id).Distinct().Count() != value.DiagnosticTargets.Length)
            throw new ArgumentException("Invalid diagnostic target list");
        foreach (var target in value.DiagnosticTargets) PixelHelper.Diagnostics.AiNetworkProbe.Validate(new(target.Url, target.AllowedAddresses));
        if (value.Models is null || value.Models.Length is < 1 or > 50 || value.Models.Any(m => m is null || string.IsNullOrWhiteSpace(m.Id) || m.Id.Length > 200 || m.Provider is null || m.Provider.Length > 100 || m.Price is not ("Free" or "Paid" or "Unknown")) || value.Models.Select(m => m.Id).Distinct().Count() != value.Models.Length)
            throw new ArgumentException("Invalid model catalog");
        if (!value.Models.Any(m => m.Id == value.DefaultModel)) throw new ArgumentException("Default model is outside the allowlist");
        if (value.RequestsPerDay is < 1 or > 10000 || value.MaxOutputTokens is < 32 or > 4096 || value.TimeoutSeconds is < 10 or > 180 || value.RetentionDays is < 1 or > 365)
            throw new ArgumentException("Invalid AI limits");
        if (value.Enabled && value.Models.Single(m => m.Id == value.DefaultModel).Price != "Free")
            throw new ArgumentException("Paid/unknown models are configured but execution is disabled until budget support is implemented");
        if (value.Models.Any(m => m.Price == "Free" && ((value.Backend == "CloudApi" && new Uri(value.BaseUrl).Host == "openrouter.ai") ||
            value.Backend == "OpenCode" && m.Provider == "openrouter") && m.Id != "openrouter/free" && !m.Id.EndsWith(":free", StringComparison.Ordinal)))
            throw new ArgumentException("Select a real OpenRouter free variant");
    }
    public static Uri ValidateUrl(string url, bool internalService)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && (uri.IsLoopback || internalService && InternalHost(uri.Host)))))
            throw new ArgumentException("Use an HTTPS API URL (HTTP is allowed for local tests/internal OpenCode)");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }
    private static bool InternalHost(string host)
    {
        if (System.Net.IPAddress.TryParse(host.Trim('[', ']'), out var address))
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            if (!PixelHelper.Diagnostics.AiNetworkProbe.IsSafeAddress(address)) return false;
            byte[] bytes = address.GetAddressBytes();
            return bytes.Length == 4 ? bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168 : (bytes[0] & 0xfe) == 0xfc;
        }
        // Docker service names; remote FQDNs require HTTPS.
        return !host.Contains('.') && Uri.CheckHostName(host) == UriHostNameType.Dns;
    }
}
