using PixelHelper.Updates;
using System.Security.Cryptography;
using System.Text.Json;

namespace LitleHelperServer;

public class ClientReleases(IConfiguration configuration)
{
    private readonly SemaphoreSlim upload = new(1);
    private string Folder => configuration["ClientUpdates:Directory"] ?? Path.Combine("data", "client-updates");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public bool Enabled => !File.Exists(Path.Combine(Folder, "disabled"));
    public void SetEnabled(bool enabled) { Directory.CreateDirectory(Folder); string path = Path.Combine(Folder, "disabled"); if (enabled) File.Delete(path); else File.WriteAllText(path, "disabled"); }
    public ClientUpdateManifest? Latest()
    {
        string path = Path.Combine(Folder, "latest.json"); if (!File.Exists(path)) return null;
        var manifest = JsonSerializer.Deserialize<ClientUpdateManifest>(File.ReadAllText(path), Json);
        return manifest?.Valid() == true ? manifest : null;
    }
    public string Package(ClientUpdateManifest manifest) => Path.GetFullPath(Path.Combine(Folder, manifest.Sha256, "PixelHelper.msi"));
    public async Task PublishAsync(IFormFile msi, IFormFile json, CancellationToken token)
    {
        if (json.Length > 8192 || msi.Length is <= 0 or > ClientUpdateManifest.MaximumSize) throw new ArgumentException("Неверный размер пакета или манифеста.");
        using var stream = json.OpenReadStream();
        ClientUpdateManifest? manifest;
        try { manifest = await JsonSerializer.DeserializeAsync<ClientUpdateManifest>(stream, Json, token); }
        catch (JsonException) { throw new ArgumentException("Некорректный JSON манифеста обновления."); }
        if (manifest?.Valid() != true || manifest.Size != msi.Length) throw new ArgumentException("Подпись манифеста, версия или размер MSI не прошли проверку.");
        await upload.WaitAsync(token);
        string temporary = "";
        try
        {
            var old = Latest();
            if (old != null && System.Version.Parse(manifest.Version) <= System.Version.Parse(old.Version)) throw new ArgumentException("Опубликуйте версию новее текущей. Откат и повторная публикация версии запрещены.");
            Directory.CreateDirectory(Folder); temporary = Path.Combine(Folder, "upload-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
            string package = Path.Combine(temporary, "PixelHelper.msi");
            await using (var output = new FileStream(package, FileMode.CreateNew, FileAccess.Write, FileShare.None)) await msi.CopyToAsync(output, token);
            await using (var input = File.OpenRead(package))
            {
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token)).ToLowerInvariant();
                if (hash != manifest.Sha256 || input.Length != manifest.Size) throw new ArgumentException("Контрольная сумма MSI не совпадает с подписанным манифестом.");
            }
            string destination = Path.Combine(Folder, manifest.Sha256);
            if (!Directory.Exists(destination)) Directory.Move(temporary, destination);
            string pointer = Path.Combine(Folder, "latest.json"); await File.WriteAllTextAsync(pointer + ".tmp", JsonSerializer.Serialize(manifest, Json), token); File.Move(pointer + ".tmp", pointer, true);
        }
        finally
        {
            if (temporary.Length > 0 && Directory.Exists(temporary)) Directory.Delete(temporary, true);
            upload.Release();
        }
    }
}
