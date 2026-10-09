using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PixelHelper;

public sealed record CompanionMessage(DateTime Timestamp, bool IsUser, string Text, string Emotion = "Normal");

public static class CompanionStorage
{
    private static readonly object sync = new();
    private static string? customFilePath;

    internal static void SetCustomFilePathForTesting(string? path)
    {
        lock (sync)
        {
            customFilePath = path;
        }
    }

    public static string GetFilePath()
    {
        lock (sync)
        {
            if (customFilePath != null) return customFilePath;
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "PixelLittleHelper");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "companion_chat.dat");
        }
    }

    public static List<CompanionMessage> Load()
    {
        lock (sync)
        {
            string path = GetFilePath();
            if (!File.Exists(path)) return new List<CompanionMessage>();

            try
            {
                byte[] raw = File.ReadAllBytes(path);
                if (raw.Length == 0) return new List<CompanionMessage>();

                byte[] decrypted;
                try
                {
                    decrypted = ProtectedData.Unprotect(raw, null, DataProtectionScope.CurrentUser);
                }
                catch
                {
                    // If not DPAPI-encrypted or legacy/fallback
                    decrypted = raw;
                }

                string json = Encoding.UTF8.GetString(decrypted);
                var list = JsonSerializer.Deserialize<List<CompanionMessage>>(json);
                return list ?? new List<CompanionMessage>();
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
                return new List<CompanionMessage>();
            }
        }
    }

    public static void Save(IEnumerable<CompanionMessage> messages)
    {
        lock (sync)
        {
            try
            {
                string path = GetFilePath();
                string json = JsonSerializer.Serialize(messages);
                byte[] raw = Encoding.UTF8.GetBytes(json);

                byte[] encrypted;
                try
                {
                    encrypted = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
                }
                catch
                {
                    encrypted = raw;
                }

                File.WriteAllBytes(path, encrypted);
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
            }
        }
    }

    public static void Append(CompanionMessage message)
    {
        lock (sync)
        {
            var list = Load();
            list.Add(message);
            Save(list);
        }
    }

    public static void Clear()
    {
        lock (sync)
        {
            try
            {
                string path = GetFilePath();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
            }
        }
    }

    public static int AutoPurge(TimeSpan? maxAge = null)
    {
        lock (sync)
        {
            try
            {
                var cutoff = DateTime.UtcNow - (maxAge ?? TimeSpan.FromDays(30));
                var list = Load();
                int originalCount = list.Count;
                var filtered = list.Where(m => m.Timestamp >= cutoff).ToList();
                if (filtered.Count != originalCount)
                {
                    Save(filtered);
                }
                return originalCount - filtered.Count;
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
                return 0;
            }
        }
    }
}
