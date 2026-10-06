using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;

namespace LitleHelperServer;

public sealed record GlpiOptions(string BaseUrl, string AppToken, string UserToken, int ServiceUserId, string AuthMode = "token", string Login = "", string Password = "");
public sealed record GlpiSettingsUpdate(string BaseUrl, string? AppToken, string? UserToken, int ServiceUserId, string AuthMode = "token", string Login = "", string? Password = null, bool ClearAppToken = false, bool ClearUserToken = false, bool ClearPassword = false);

public sealed class GlpiSettingsStore(IConfiguration config, IDataProtectionProvider protection)
{
    private readonly object gate = new();
    private readonly IDataProtector protector = protection.CreateProtector("LitleHelper.GLPI.Settings.v1");
    private string FilePath => config["Glpi:SettingsFile"] ?? Path.Combine("data", "glpi-settings.json");
    private sealed record Stored(string BaseUrl, string AppToken, string UserToken, int ServiceUserId, string AuthMode, string Login, string Password);

    public GlpiOptions Read()
    {
        lock (gate)
        {
            if (!File.Exists(FilePath)) return new(config["Glpi:BaseUrl"] ?? "http://glpi.gp1.loc/apirest.php", config["Glpi:AppToken"] ?? "", config["Glpi:UserToken"] ?? "", config.GetValue<int>("Glpi:ServiceUserId"));
            var saved = JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath)) ?? throw new InvalidOperationException("Не удалось прочитать настройки GLPI");
            string Decode(string value) => value.Length == 0 ? "" : protector.Unprotect(value);
            return new(saved.BaseUrl, Decode(saved.AppToken), Decode(saved.UserToken), saved.ServiceUserId, saved.AuthMode, saved.Login, Decode(saved.Password));
        }
    }
    public object View()
    {
        var value = Read();
        return new { value.BaseUrl, value.ServiceUserId, value.AuthMode, value.Login, hasAppToken = value.AppToken.Length > 0, hasUserToken = value.UserToken.Length > 0, hasPassword = value.Password.Length > 0 };
    }
    public void Save(GlpiSettingsUpdate update)
    {
        if (!Uri.TryCreate(update.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || update.BaseUrl.Length > 1000)
            throw new ArgumentException("Укажите HTTP/HTTPS-адрес GLPI без пароля, query и fragment");
        if (update.AuthMode is not ("token" or "password") || update.ServiceUserId < 0 || update.Login.Length > 150 || update.Login.Contains(':')) throw new ArgumentException("Неверные параметры доступа GLPI");
        foreach (string? secret in new[] { update.AppToken, update.UserToken, update.Password })
            if (secret?.Length > 2000 || secret?.Contains('\r') == true || secret?.Contains('\n') == true) throw new ArgumentException("Неверный формат секрета GLPI");
        string url = uri.AbsoluteUri.TrimEnd('/');
        if (!url.EndsWith("/apirest.php", StringComparison.OrdinalIgnoreCase)) url += "/apirest.php";
        lock (gate)
        {
            var old = Read();
            string Merge(string? value, string previous, bool clear) => clear ? "" : string.IsNullOrEmpty(value) ? previous : value;
            string Encode(string value) => value.Length == 0 ? "" : protector.Protect(value);
            var saved = new Stored(url, Encode(Merge(update.AppToken, old.AppToken, update.ClearAppToken)), Encode(Merge(update.UserToken, old.UserToken, update.ClearUserToken)), update.ServiceUserId,
                update.AuthMode, update.Login.Trim(), Encode(Merge(update.Password, old.Password, update.ClearPassword)));
            string path = Path.GetFullPath(FilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(saved));
            File.Move(path + ".tmp", path, true);
        }
    }
}
