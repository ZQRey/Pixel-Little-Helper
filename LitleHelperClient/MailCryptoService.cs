using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace PixelHelper;

public sealed record MailCertificateInfo(string Thumbprint, string Subject, string Email, DateTime NotAfter);

public static class MailCryptoService
{
    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("PixelHelper_Mail_DPAPI_v1");
    private static readonly byte[] FileMagic = Encoding.ASCII.GetBytes("PXMGCM01");
    public const string BeginArmor = "-----BEGIN PIXELHELPER ENCRYPTED MESSAGE-----";
    public const string EndArmor = "-----END PIXELHELPER ENCRYPTED MESSAGE-----";
    private static readonly object keyLock = new();
    private static byte[]? cachedMasterKey;
    private static string? cachedMasterKeyFolder;

    public static string ProtectSecret(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";
        try
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] encrypted = ProtectedData.Protect(plainBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
            return "";
        }
    }

    public static string UnprotectSecret(string? protectedBase64)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64)) return "";
        try
        {
            byte[] encrypted = Convert.FromBase64String(protectedBase64);
            byte[] plainBytes = ProtectedData.Unprotect(encrypted, DpapiEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return "";
        }
    }

    private static byte[] GetMasterKey()
    {
        lock (keyLock)
        {
            string folder = Settings.Folder;
            if (cachedMasterKey != null && cachedMasterKeyFolder == folder)
            {
                return cachedMasterKey;
            }

            Directory.CreateDirectory(folder);
            string keyFile = Path.Combine(folder, "mail_master.key");
            if (File.Exists(keyFile))
            {
                try
                {
                    byte[] protectedBytes = File.ReadAllBytes(keyFile);
                    byte[] rawKey = ProtectedData.Unprotect(protectedBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
                    if (rawKey.Length == 32)
                    {
                        cachedMasterKey = rawKey;
                        cachedMasterKeyFolder = folder;
                        return rawKey;
                    }
                }
                catch { }
            }

            byte[] newKey = RandomNumberGenerator.GetBytes(32);
            try
            {
                byte[] protectedNew = ProtectedData.Protect(newKey, DpapiEntropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(keyFile, protectedNew);
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
            }

            cachedMasterKey = newKey;
            cachedMasterKeyFolder = folder;
            return newKey;
        }
    }

    public static byte[] EncryptBytes(byte[] plainBytes)
    {
        byte[] key = GetMasterKey();
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] tag = new byte[16];
        byte[] cipher = new byte[plainBytes.Length];

        using (var aes = new AesGcm(key, 16))
        {
            aes.Encrypt(nonce, plainBytes, cipher, tag, FileMagic);
        }

        byte[] output = new byte[FileMagic.Length + nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(FileMagic, 0, output, 0, FileMagic.Length);
        Buffer.BlockCopy(nonce, 0, output, FileMagic.Length, nonce.Length);
        Buffer.BlockCopy(tag, 0, output, FileMagic.Length + nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, output, FileMagic.Length + nonce.Length + tag.Length, cipher.Length);
        return output;
    }

    public static byte[] DecryptBytes(byte[] encryptedBytes)
    {
        if (encryptedBytes.Length < FileMagic.Length + 12 + 16)
            throw new CryptographicException("Поврежденный зашифрованный контейнер.");

        for (int i = 0; i < FileMagic.Length; i++)
        {
            if (encryptedBytes[i] != FileMagic[i])
                throw new CryptographicException("Неверный заголовок зашифрованного файла.");
        }

        byte[] key = GetMasterKey();
        byte[] nonce = new byte[12];
        byte[] tag = new byte[16];
        int cipherLen = encryptedBytes.Length - (FileMagic.Length + 12 + 16);
        byte[] cipher = new byte[cipherLen];
        byte[] plain = new byte[cipherLen];

        Buffer.BlockCopy(encryptedBytes, FileMagic.Length, nonce, 0, 12);
        Buffer.BlockCopy(encryptedBytes, FileMagic.Length + 12, tag, 0, 16);
        Buffer.BlockCopy(encryptedBytes, FileMagic.Length + 12 + 16, cipher, 0, cipherLen);

        using (var aes = new AesGcm(key, 16))
        {
            aes.Decrypt(nonce, cipher, tag, plain, FileMagic);
        }

        return plain;
    }

    public static void EncryptToFile(byte[] plainBytes, string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        byte[] encrypted = EncryptBytes(plainBytes);
        File.WriteAllBytes(filePath, encrypted);
    }

    public static byte[] DecryptFromFile(string filePath)
    {
        byte[] encrypted = File.ReadAllBytes(filePath);
        return DecryptBytes(encrypted);
    }

    public static bool IsPixelAesEncrypted(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains(BeginArmor, StringComparison.Ordinal) &&
               text.Contains(EndArmor, StringComparison.Ordinal);
    }

    public static string EncryptMessagePixelAes(string plainText, string passphrase)
    {
        string effectiveKey = string.IsNullOrWhiteSpace(passphrase) ? "PixelHelper_Default_Corp_Key_2026" : passphrase.Trim();
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] tag = new byte[16];
        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText ?? "");
        byte[] cipherBytes = new byte[plainBytes.Length];

        byte[] derivedKey = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(effectiveKey),
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            32);

        using (var aes = new AesGcm(derivedKey, 16))
        {
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag, Encoding.ASCII.GetBytes("PIXEL-E2E-V1"));
        }

        byte[] payload = new byte[16 + 12 + 16 + cipherBytes.Length];
        Buffer.BlockCopy(salt, 0, payload, 0, 16);
        Buffer.BlockCopy(nonce, 0, payload, 16, 12);
        Buffer.BlockCopy(tag, 0, payload, 28, 16);
        Buffer.BlockCopy(cipherBytes, 0, payload, 44, cipherBytes.Length);

        string base64 = Convert.ToBase64String(payload, Base64FormattingOptions.InsertLineBreaks);
        return $"{BeginArmor}\r\n{base64}\r\n{EndArmor}";
    }

    public static bool TryDecryptMessagePixelAes(string rawText, string passphrase, out string decryptedText)
    {
        decryptedText = "";
        if (string.IsNullOrWhiteSpace(rawText)) return false;

        int startIdx = rawText.IndexOf(BeginArmor, StringComparison.Ordinal);
        int endIdx = rawText.IndexOf(EndArmor, StringComparison.Ordinal);
        if (startIdx < 0 || endIdx <= startIdx) return false;

        string base64Part = rawText.Substring(startIdx + BeginArmor.Length, endIdx - (startIdx + BeginArmor.Length)).Trim();
        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(base64Part);
        }
        catch
        {
            return false;
        }

        if (payload.Length < 44) return false;

        byte[] salt = new byte[16];
        byte[] nonce = new byte[12];
        byte[] tag = new byte[16];
        int cipherLen = payload.Length - 44;
        byte[] cipherBytes = new byte[cipherLen];
        byte[] plainBytes = new byte[cipherLen];

        Buffer.BlockCopy(payload, 0, salt, 0, 16);
        Buffer.BlockCopy(payload, 16, nonce, 0, 12);
        Buffer.BlockCopy(payload, 28, tag, 0, 16);
        Buffer.BlockCopy(payload, 44, cipherBytes, 0, cipherLen);

        string[] candidates = string.IsNullOrWhiteSpace(passphrase)
            ? ["PixelHelper_Default_Corp_Key_2026"]
            : [passphrase.Trim(), "PixelHelper_Default_Corp_Key_2026"];

        foreach (string candidate in candidates)
        {
            try
            {
                byte[] derivedKey = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(candidate),
                    salt,
                    100_000,
                    HashAlgorithmName.SHA256,
                    32);

                using var aes = new AesGcm(derivedKey, 16);
                aes.Decrypt(nonce, cipherBytes, tag, plainBytes, Encoding.ASCII.GetBytes("PIXEL-E2E-V1"));
                decryptedText = Encoding.UTF8.GetString(plainBytes);
                return true;
            }
            catch
            {
                // Try next key candidate
            }
        }

        return false;
    }

    public static List<MailCertificateInfo> GetAvailableCertificates()
    {
        var result = new List<MailCertificateInfo>();
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            foreach (var cert in store.Certificates)
            {
                if (!cert.HasPrivateKey) continue;
                string subject = cert.GetNameInfo(X509NameType.SimpleName, false) ?? cert.Subject;
                string email = cert.GetNameInfo(X509NameType.EmailName, false) ?? "";
                result.Add(new MailCertificateInfo(cert.Thumbprint, subject, email, cert.NotAfter));
            }
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
        return result;
    }

    public static async Task<byte[]> CreateEncryptedMailBackupAsync(
        string accountId,
        string accountEmail,
        string archivePassword,
        MailStorageEngine storage,
        CancellationToken token = default)
    {
        var messages = storage.GetAccountMessages(accountId);
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
        {
            var manifest = new
            {
                AccountId = accountId,
                AccountEmail = accountEmail,
                ExportDateUtc = DateTime.UtcNow,
                MessageCount = messages.Count,
                VerificationToken = "PIXELMAIL_VALID_V1"
            };
            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using (var entryStream = manifestEntry.Open())
            await using (var writer = new StreamWriter(entryStream, Encoding.UTF8))
            {
                await writer.WriteAsync(JsonSerializer.Serialize(manifest));
            }

            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                byte[] rawMime = storage.GetMessageRawBytes(msg);
                string entryName = $"messages/msg_{i:D6}_{msg.Id}.eml";
                var emlEntry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                await using (var entryStream = emlEntry.Open())
                {
                    await entryStream.WriteAsync(rawMime.AsMemory(), token);
                }
            }
        }

        byte[] plainZip = zipStream.ToArray();

        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] tag = new byte[16];
        byte[] derivedKey = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(archivePassword),
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            32);

        byte[] cipher = new byte[plainZip.Length];
        using (var aes = new AesGcm(derivedKey, 16))
        {
            aes.Encrypt(nonce, plainZip, cipher, tag, Encoding.ASCII.GetBytes("PXMBKP01"));
        }

        byte[] output = new byte[8 + 16 + 12 + 16 + cipher.Length];
        Buffer.BlockCopy(Encoding.ASCII.GetBytes("PXMBKP01"), 0, output, 0, 8);
        Buffer.BlockCopy(salt, 0, output, 8, 16);
        Buffer.BlockCopy(nonce, 0, output, 24, 12);
        Buffer.BlockCopy(tag, 0, output, 36, 16);
        Buffer.BlockCopy(cipher, 0, output, 52, cipher.Length);

        return output;
    }

    public static async Task<(bool Success, int RestoredCount, string Error)> RestoreFromEncryptedMailBackupAsync(
        byte[] encryptedPackage,
        string archivePassword,
        MailAccountConfig account,
        MailStorageEngine storage,
        CancellationToken token = default)
    {
        if (encryptedPackage.Length < 8 + 16 + 12 + 16)
            return (false, 0, "Некорректный или слишком короткий файл архива.");

        string magic = Encoding.ASCII.GetString(encryptedPackage, 0, 8);
        if (magic != "PXMBKP01")
            return (false, 0, "Неверный формат архива PixelMail.");

        byte[] salt = new byte[16];
        byte[] nonce = new byte[12];
        byte[] tag = new byte[16];
        Buffer.BlockCopy(encryptedPackage, 8, salt, 0, 16);
        Buffer.BlockCopy(encryptedPackage, 24, nonce, 0, 12);
        Buffer.BlockCopy(encryptedPackage, 36, tag, 0, 16);

        int cipherLen = encryptedPackage.Length - 52;
        byte[] cipher = new byte[cipherLen];
        byte[] plainZip = new byte[cipherLen];
        Buffer.BlockCopy(encryptedPackage, 52, cipher, 0, cipherLen);

        byte[] derivedKey = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(archivePassword),
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            32);

        try
        {
            using var aes = new AesGcm(derivedKey, 16);
            aes.Decrypt(nonce, cipher, tag, plainZip, Encoding.ASCII.GetBytes("PXMBKP01"));
        }
        catch (CryptographicException)
        {
            return (false, 0, "Неверный пароль архива или поврежденные данные.");
        }

        int restored = 0;
        using var zipStream = new MemoryStream(plainZip);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.StartsWith("messages/") && entry.FullName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase))
            {
                using var entryStream = entry.Open();
                using var ms = new MemoryStream();
                await entryStream.CopyToAsync(ms, token);
                byte[] rawMime = ms.ToArray();
                string uid = "restored_" + Guid.NewGuid().ToString("N")[..8];
                await storage.IngestRawMimeAsync(account, uid, rawMime, MailFolderKind.Inbox, token);
                restored++;
            }
        }

        return (true, restored, "");
    }
}

