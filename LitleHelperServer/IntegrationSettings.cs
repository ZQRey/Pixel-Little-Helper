using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LitleHelperServer;

public record TelegramOptions(bool Enabled = false, string BotToken = "", string ChatId = "", int ThreadId = 0);
public record TelegramUpdate(bool Enabled, string? BotToken, string ChatId, int ThreadId = 0, bool ClearBotToken = false);
public record AdOptions(bool Enabled = false, string Host = "", int Port = 636, string Domain = "", string NetbiosDomain = "", string BaseDn = "", string CaCertificate = "");

public class IntegrationSettings(IConfiguration configuration, IDataProtectionProvider protection)
{
    private readonly object gate = new();
    private readonly IDataProtector protector = protection.CreateProtector("LitleHelper.Integrations.v1");
    private string PathName => configuration["Integrations:SettingsFile"] ?? Path.Combine("data", "integrations.json");
    private record Stored(TelegramOptions Telegram, AdOptions Ad);
    private Stored ReadStored() => File.Exists(PathName) ? JsonSerializer.Deserialize<Stored>(File.ReadAllText(PathName))! : new(new(), new());
    public TelegramOptions Telegram() { lock (gate) { var value = ReadStored().Telegram; return value with { BotToken = value.BotToken.Length == 0 ? "" : protector.Unprotect(value.BotToken) }; } }
    public object TelegramView() { var v = Telegram(); return new { v.Enabled, v.ChatId, v.ThreadId, hasBotToken = v.BotToken.Length > 0 }; }
    public AdOptions Ad() { lock (gate) return ReadStored().Ad; }
    private void Write(Stored value)
    {
        string path = Path.GetFullPath(PathName); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value)); File.Move(path + ".tmp", path, true);
    }
    public void SaveTelegram(TelegramUpdate update)
    {
        if (update.ThreadId < 0 || update.ChatId.Length > 100 || update.BotToken?.Length > 300 ||
            (update.ChatId.Length > 0 && !Regex.IsMatch(update.ChatId, @"^-?\d+$|^@[A-Za-z0-9_]+$")) ||
            (!string.IsNullOrEmpty(update.BotToken) && !Regex.IsMatch(update.BotToken, @"^\d+:[A-Za-z0-9_-]+$")))
            throw new ArgumentException("Проверьте токен, Chat ID и ID темы Telegram.");
        lock (gate)
        {
            var old = ReadStored(); var token = update.ClearBotToken ? "" : string.IsNullOrEmpty(update.BotToken) ? old.Telegram.BotToken : protector.Protect(update.BotToken);
            if (update.Enabled && (token.Length == 0 || update.ChatId.Length == 0)) throw new ArgumentException("Для включения Telegram нужны токен бота и Chat ID.");
            Write(old with { Telegram = new(update.Enabled, token, update.ChatId, update.ThreadId) });
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
