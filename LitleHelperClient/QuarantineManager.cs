using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace PixelHelper;

public sealed record QuarantinedItem(
    string Id,
    string OriginalPath,
    string QuarantinedPath,
    string Sha256,
    string ThreatName,
    string ThreatType,
    long FileSizeBytes,
    DateTime QuarantinedAt,
    DateTime? QuarantinedUntil
);

public sealed record WhitelistItem(
    string Sha256,
    string FilePath,
    DateTime AddedAt
);

public sealed class QuarantineManager
{
    private static readonly byte[] XorKey = [0x50, 0x69, 0x78, 0x65, 0x6C, 0x47, 0x75, 0x61, 0x72, 0x64, 0x41, 0x56]; // "PixelGuardAV"
    private readonly object gate = new();

    public static string? OverrideBaseDir { get; set; }
    public static string BaseDir => OverrideBaseDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PixelHelper");
    public static string QuarantineDir => Path.Combine(BaseDir, "Quarantine");
    private static string IndexFilePath => Path.Combine(QuarantineDir, "quarantine_index.json");
    private static string WhitelistFilePath => Path.Combine(BaseDir, "trusted_exclusions.json");

    public QuarantineManager()
    {
        try
        {
            Directory.CreateDirectory(QuarantineDir);
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    public List<QuarantinedItem> GetQuarantinedItems()
    {
        lock (gate)
        {
            return ReadIndex();
        }
    }

    public bool IsWhitelisted(string filePath, string? sha256 = null)
    {
        lock (gate)
        {
            var whitelist = ReadWhitelist();
            if (whitelist.Count == 0) return false;

            if (!string.IsNullOrEmpty(sha256) && whitelist.Any(w => string.Equals(w.Sha256, sha256, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(filePath) && whitelist.Any(w => string.Equals(w.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }
    }

    public void AddToWhitelist(string sha256, string filePath)
    {
        lock (gate)
        {
            var list = ReadWhitelist();
            if (!list.Any(w => string.Equals(w.Sha256, sha256, StringComparison.OrdinalIgnoreCase) &&
                               string.Equals(w.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(new WhitelistItem(sha256, filePath, DateTime.UtcNow));
                WriteWhitelist(list);
            }
        }
    }

    public QuarantinedItem? QuarantineFile(string filePath, string threatName, string threatType, int retentionDays)
    {
        lock (gate)
        {
            try
            {
                if (!File.Exists(filePath)) return null;

                var fi = new FileInfo(filePath);
                long fileSizeBytes = fi.Length;
                string sha256 = ComputeSha256(filePath);

                // If already whitelisted, do not quarantine
                if (IsWhitelisted(filePath, sha256))
                {
                    return null;
                }

                Directory.CreateDirectory(QuarantineDir);
                string id = Guid.NewGuid().ToString("N");
                string quarantinePath = Path.Combine(QuarantineDir, id + ".quarantine");

                // Encrypt file into quarantine
                EncryptFile(filePath, quarantinePath);

                // Remove original malicious file
                File.Delete(filePath);

                DateTime now = DateTime.UtcNow;
                DateTime? until = retentionDays > 0 ? now.AddDays(retentionDays) : null;

                var item = new QuarantinedItem(
                    Id: id,
                    OriginalPath: filePath,
                    QuarantinedPath: quarantinePath,
                    Sha256: sha256,
                    ThreatName: threatName,
                    ThreatType: threatType,
                    FileSizeBytes: fileSizeBytes,
                    QuarantinedAt: now,
                    QuarantinedUntil: until
                );

                var items = ReadIndex();
                items.Add(item);
                WriteIndex(items);

                return item;
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
                return null;
            }
        }
    }

    public bool RestoreFile(string id)
    {
        lock (gate)
        {
            var item = ReadIndex().FirstOrDefault(i => i.Id == id);
            return item != null && RestoreFile(item.Sha256, item.OriginalPath);
        }
    }

    public bool RestoreFile(string sha256, string originalPath)
    {
        lock (gate)
        {
            try
            {
                var items = ReadIndex();
                var item = items.FirstOrDefault(i =>
                    string.Equals(i.Sha256, sha256, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(i.OriginalPath, originalPath, StringComparison.OrdinalIgnoreCase));

                if (item == null || !File.Exists(item.QuarantinedPath))
                {
                    return false;
                }

                string targetDir = Path.GetDirectoryName(item.OriginalPath)!;
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                // Decrypt file back to original path
                DecryptFile(item.QuarantinedPath, item.OriginalPath);

                // Clean up quarantine vault
                File.Delete(item.QuarantinedPath);
                items.Remove(item);
                WriteIndex(items);

                // CRITICAL: Automatically add to whitelist to prevent re-quarantining on future scans!
                AddToWhitelist(item.Sha256, item.OriginalPath);

                return true;
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
                return false;
            }
        }
    }

    public bool DeletePermanently(string id)
    {
        lock (gate)
        {
            var item = ReadIndex().FirstOrDefault(i => i.Id == id);
            return item != null && DeletePermanently(item.Sha256, item.OriginalPath);
        }
    }

    public bool DeletePermanently(string sha256, string originalPath)
    {
        lock (gate)
        {
            try
            {
                var items = ReadIndex();
                var item = items.FirstOrDefault(i =>
                    string.Equals(i.Sha256, sha256, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(i.OriginalPath, originalPath, StringComparison.OrdinalIgnoreCase));

                if (item == null) return false;

                if (File.Exists(item.QuarantinedPath))
                {
                    // Secure zeroing
                    SecureWipe(item.QuarantinedPath);
                }

                items.Remove(item);
                WriteIndex(items);
                return true;
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
                return false;
            }
        }
    }

    public int RunAutoPurge()
    {
        lock (gate)
        {
            int purged = 0;
            try
            {
                var items = ReadIndex();
                var now = DateTime.UtcNow;
                var toRemove = new List<QuarantinedItem>();

                foreach (var item in items)
                {
                    if (item.QuarantinedUntil.HasValue && now >= item.QuarantinedUntil.Value)
                    {
                        if (File.Exists(item.QuarantinedPath))
                        {
                            SecureWipe(item.QuarantinedPath);
                        }
                        toRemove.Add(item);
                        purged++;
                    }
                }

                if (toRemove.Count > 0)
                {
                    foreach (var rem in toRemove) items.Remove(rem);
                    WriteIndex(items);
                }
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
            }
            return purged;
        }
    }

    private static void SecureWipe(string path)
    {
        try
        {
            long len = new FileInfo(path).Length;
            byte[] zeroes = new byte[Math.Min(len, 65536)];
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                long written = 0;
                while (written < len)
                {
                    int chunk = (int)Math.Min(zeroes.Length, len - written);
                    fs.Write(zeroes, 0, chunk);
                    written += chunk;
                }
                fs.Flush();
            }
            File.Delete(path);
        }
        catch
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static void EncryptFile(string src, string dst)
    {
        byte[] buffer = new byte[65536];
        using var inFs = File.OpenRead(src);
        using var outFs = File.Create(dst);

        int read;
        long pos = 0;
        while ((read = inFs.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                buffer[i] ^= XorKey[(pos + i) % XorKey.Length];
            }
            outFs.Write(buffer, 0, read);
            pos += read;
        }
    }

    private static void DecryptFile(string src, string dst)
    {
        // XOR is symmetric
        EncryptFile(src, dst);
    }

    public static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs));
    }

    private static List<QuarantinedItem> ReadIndex()
    {
        try
        {
            if (!File.Exists(IndexFilePath)) return new();
            string json = File.ReadAllText(IndexFilePath);
            return JsonSerializer.Deserialize<List<QuarantinedItem>>(json, Settings.Json) ?? new();
        }
        catch { return new(); }
    }

    private static void WriteIndex(List<QuarantinedItem> items)
    {
        try
        {
            string json = JsonSerializer.Serialize(items, Settings.Json);
            Settings.AtomicWrite(IndexFilePath, json);
        }
        catch { }
    }

    private static List<WhitelistItem> ReadWhitelist()
    {
        try
        {
            if (!File.Exists(WhitelistFilePath)) return new();
            string json = File.ReadAllText(WhitelistFilePath);
            return JsonSerializer.Deserialize<List<WhitelistItem>>(json, Settings.Json) ?? new();
        }
        catch { return new(); }
    }

    private static void WriteWhitelist(List<WhitelistItem> list)
    {
        try
        {
            string json = JsonSerializer.Serialize(list, Settings.Json);
            Settings.AtomicWrite(WhitelistFilePath, json);
        }
        catch { }
    }
}
