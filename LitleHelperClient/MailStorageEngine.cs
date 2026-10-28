using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PixelHelper;

public sealed class MailStorageEngine
{
    private readonly Settings settings;
    private readonly AntivirusService? antivirus;
    private readonly object gate = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> accountSyncGates = new(StringComparer.OrdinalIgnoreCase);
    private List<MailMessageItem> messages = new();

    public event Action<List<MailMessageItem>>? NewMessagesArrived;
    public event Action? MessagesUpdated;

    public static string StoreRoot => Path.Combine(Settings.Folder, "MailStore");
    private static string IndexPath => Path.Combine(StoreRoot, "mail_index.json");

    public MailStorageEngine(Settings settings, AntivirusService? antivirus = null)
    {
        this.settings = settings;
        this.antivirus = antivirus;
        Directory.CreateDirectory(StoreRoot);
        LoadIndex();
    }

    private void LoadIndex()
    {
        lock (gate)
        {
            try
            {
                if (File.Exists(IndexPath))
                {
                    string json = File.ReadAllText(IndexPath);
                    messages = JsonSerializer.Deserialize<List<MailMessageItem>>(json, Settings.Json) ?? new();
                }
                else
                {
                    messages = new();
                }
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
                messages = new();
            }
        }
    }

    private void SaveIndex()
    {
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(StoreRoot);
                string json = JsonSerializer.Serialize(messages, Settings.Json);
                Settings.AtomicWrite(IndexPath, json);
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
            }
        }
    }

    public int GetTotalUnreadCount()
    {
        lock (gate)
        {
            return messages.Count(m => !m.IsRead && m.Folder is MailFolderKind.Inbox or MailFolderKind.LocalArchive);
        }
    }

    public int GetUnreadCountForAccount(string accountId, MailFolderKind folder)
    {
        lock (gate)
        {
            return messages.Count(m => !m.IsRead &&
                (string.IsNullOrEmpty(accountId) || m.AccountId == accountId) &&
                (folder == MailFolderKind.UnifiedInbox ? m.Folder == MailFolderKind.Inbox : m.Folder == folder));
        }
    }

    public List<MailMessageItem> QueryMessages(
        string? accountId,
        MailFolderKind folder,
        string? searchQuery = null,
        string filterMode = "all")
    {
        lock (gate)
        {
            IEnumerable<MailMessageItem> query = messages;

            if (!string.IsNullOrEmpty(accountId))
            {
                query = query.Where(m => m.AccountId == accountId);
            }

            query = folder switch
            {
                MailFolderKind.UnifiedInbox => query.Where(m => m.Folder is MailFolderKind.Inbox or MailFolderKind.LocalArchive),
                MailFolderKind.LocalArchive => query.Where(m => m.DownloadedToPc || m.Folder == MailFolderKind.LocalArchive),
                _ => query.Where(m => m.Folder == folder)
            };

            query = filterMode switch
            {
                "unread" => query.Where(m => !m.IsRead),
                "attachments" => query.Where(m => m.HasAttachments),
                "encrypted" => query.Where(m => m.IsEncrypted),
                "threats" => query.Where(m => m.SecurityVerdict is MailSecurityVerdict.BlockedMalware or MailSecurityVerdict.PhishingWarning or MailSecurityVerdict.BlockedBlacklist or MailSecurityVerdict.Spam),
                _ => query
            };

            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                string q = searchQuery.Trim().ToLowerInvariant();
                query = query.Where(m =>
                    (m.Subject ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (m.FromName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (m.FromAddress ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (m.BodyText ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    m.Attachments.Any(a => a.FileName.Contains(q, StringComparison.OrdinalIgnoreCase)));
            }

            return query.OrderByDescending(m => m.DateUtc).ToList();
        }
    }

    public async Task<MailMessageItem> IngestRawMimeAsync(
        MailAccountConfig account,
        string remoteUid,
        byte[] rawMimeBytes,
        MailFolderKind folder = MailFolderKind.Inbox,
        CancellationToken token = default)
    {
        var parsed = MailProtocolClient.ParseMimeMessage(
            rawMimeBytes,
            account.Id,
            account.EmailAddress,
            remoteUid,
            settings.MailSharedEncryptionKey);

        var msg = parsed.Message;
        if (folder != MailFolderKind.Inbox)
        {
            msg.Folder = folder;
        }
        string accountDir = Path.Combine(StoreRoot, account.Id);
        string emlDir = Path.Combine(accountDir, account.StorageMode == MailStorageMode.DownloadAllToComputer ? "LocalArchive" : "Cache");
        string attDir = Path.Combine(accountDir, "Attachments", msg.Id);
        Directory.CreateDirectory(emlDir);

        // Save encrypted .eml copy
        string emlPath = Path.Combine(emlDir, msg.Id + ".eml.enc");
        MailCryptoService.EncryptToFile(rawMimeBytes, emlPath);
        msg.LocalEmlEncryptedPath = emlPath;
        msg.DownloadedToPc = account.StorageMode == MailStorageMode.DownloadAllToComputer;

        var attPairs = new List<(MailAttachmentItem Item, byte[] RawBytes)>();
        foreach (var rawAtt in parsed.RawAttachments)
        {
            Directory.CreateDirectory(attDir);
            var attItem = new MailAttachmentItem
            {
                FileName = rawAtt.FileName,
                ContentType = rawAtt.ContentType,
                SizeBytes = rawAtt.Content.Length
            };
            attPairs.Add((attItem, rawAtt.Content));
        }

        // Run comprehensive PixelGuard AV + Anti-Phishing + Anti-Spam + Blacklist/Whitelist check
        await MailSecurityScanner.EvaluateMessageAsync(msg, attPairs, settings, antivirus, token);

        // Save non-threat attachments encrypted to disk; threat attachments are already isolated in QuarantineManager
        foreach (var (attItem, rawBytes) in attPairs)
        {
            if (!attItem.IsThreat)
            {
                string attPath = Path.Combine(attDir, attItem.Id + ".att.enc");
                MailCryptoService.EncryptToFile(rawBytes, attPath);
                attItem.LocalEncryptedPath = attPath;
            }
            msg.Attachments.Add(attItem);
        }

        lock (gate)
        {
            messages.RemoveAll(m => m.AccountId == account.Id && !string.IsNullOrEmpty(remoteUid) && m.RemoteUid == remoteUid);
            messages.Add(msg);
            SaveIndex();
        }

        MessagesUpdated?.Invoke();
        return msg;
    }

    public async Task<List<MailMessageItem>> SyncAccountAsync(MailAccountConfig account, CancellationToken token = default)
    {
        var newlyArrived = new List<MailMessageItem>();
        if (!account.IsEnabled || string.IsNullOrWhiteSpace(account.IncomingHost))
            return newlyArrived;

        var syncGate = accountSyncGates.GetOrAdd(account.Id, _ => new SemaphoreSlim(1, 1));
        if (!await syncGate.WaitAsync(TimeSpan.FromSeconds(15), token))
            return newlyArrived;

        try
        {
            HashSet<string> knownUids;
            lock (gate)
            {
                knownUids = messages
                    .Where(m => m.AccountId == account.Id && !string.IsNullOrEmpty(m.RemoteUid))
                    .Select(m => m.RemoteUid)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            string plainPassword = MailCryptoService.UnprotectSecret(account.EncryptedPassword);
            try
            {
                var fetched = await MailProtocolClient.FetchNewMessagesAsync(account, plainPassword, knownUids, 40, token);
                foreach (var (uid, rawMime, folder) in fetched)
                {
                    var item = await IngestRawMimeAsync(account, uid, rawMime, folder, token);
                    newlyArrived.Add(item);
                }

                account.LastSyncUtc = DateTime.UtcNow;
                account.LastSyncStatus = newlyArrived.Count > 0
                    ? $"Получено новых писем: {newlyArrived.Count} ({DateTime.Now:HH:mm:ss})"
                    : $"Синхронизировано ({DateTime.Now:HH:mm:ss})";
                settings.Save();
            }
            catch (Exception ex)
            {
                account.LastSyncStatus = "Ошибка: " + ex.Message;
                Settings.Log(ex);
            }
        }
        finally
        {
            syncGate.Release();
        }

        var notifyList = newlyArrived
            .Where(m => m.Folder is MailFolderKind.Inbox or MailFolderKind.LocalArchive &&
                        m.SecurityVerdict is MailSecurityVerdict.Clean or MailSecurityVerdict.TrustedWhitelist or MailSecurityVerdict.PhishingWarning or MailSecurityVerdict.BlockedMalware)
            .ToList();

        if (notifyList.Count > 0)
        {
            NewMessagesArrived?.Invoke(notifyList);
        }

        if (newlyArrived.Count > 0)
        {
            MessagesUpdated?.Invoke();
        }

        return newlyArrived;
    }

    public async Task<List<MailMessageItem>> SyncAllAccountsAsync(CancellationToken token = default)
    {
        var allNew = new List<MailMessageItem>();
        var accounts = settings.MailAccounts.Where(a => a.IsEnabled).ToList();
        foreach (var acc in accounts)
        {
            var batch = await SyncAccountAsync(acc, token);
            allNew.AddRange(batch);
        }
        return allNew;
    }

    public static string CleanEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "account";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (char c in email)
        {
            if (!invalid.Contains(c) && c != ' ' && c != '/' && c != '\\') sb.Append(c);
            else sb.Append('_');
        }
        string res = sb.ToString();
        return string.IsNullOrWhiteSpace(res) ? "account" : res;
    }

    public static string GetDefaultOffloadDirectory(string? accountEmail = null)
    {
        string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PixelHelper", "OffloadedMail");
        if (string.IsNullOrWhiteSpace(accountEmail)) return baseDir;
        return Path.Combine(baseDir, CleanEmail(accountEmail));
    }

    public (int Count, string DestinationPath) OffloadAccountToComputer(
        string? accountId,
        string? exportDirectory = null,
        MailOffloadFormat format = MailOffloadFormat.IndividualEml,
        string? accountEmail = null)
    {
        int count = 0;
        string targetDir = !string.IsNullOrWhiteSpace(exportDirectory)
            ? exportDirectory
            : GetDefaultOffloadDirectory(accountEmail);

        Directory.CreateDirectory(targetDir);
        string finalPath = targetDir;

        lock (gate)
        {
            var targetMessages = messages
                .Where(m => string.IsNullOrEmpty(accountId) || m.AccountId == accountId)
                .OrderBy(m => m.DateUtc)
                .ToList();

            if (format == MailOffloadFormat.SingleFileMbox)
            {
                string safeName = CleanEmail(!string.IsNullOrWhiteSpace(accountEmail) ? accountEmail : "all_accounts");
                finalPath = Path.Combine(targetDir, $"{safeName}_archive.mbox");
                using var fs = File.Create(finalPath);
                using var writer = new StreamWriter(fs, Encoding.UTF8);

                foreach (var msg in targetMessages)
                {
                    msg.DownloadedToPc = true;
                    string fromAddr = !string.IsNullOrWhiteSpace(msg.FromAddress) ? msg.FromAddress : "MAILER-DAEMON";
                    writer.WriteLine($"From {fromAddr} {msg.DateUtc:ddd MMM dd HH:mm:ss yyyy}");

                    string content;
                    if (!string.IsNullOrWhiteSpace(msg.LocalEmlEncryptedPath) && File.Exists(msg.LocalEmlEncryptedPath))
                    {
                        byte[] rawBytes = MailCryptoService.DecryptFromFile(msg.LocalEmlEncryptedPath);
                        content = Encoding.UTF8.GetString(rawBytes);
                    }
                    else
                    {
                        content = $"From: {msg.SenderDisplay}\r\nTo: {msg.ToDisplay}\r\nSubject: {msg.Subject}\r\nDate: {msg.DateUtc:R}\r\nMessage-ID: <{msg.Id}@pixelhelper.local>\r\n\r\n{msg.BodyText}";
                    }

                    using var sr = new StringReader(content);
                    string? line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.StartsWith("From ")) writer.WriteLine(">" + line);
                        else writer.WriteLine(line);
                    }
                    writer.WriteLine();
                    count++;
                }
            }
            else
            {
                foreach (var msg in targetMessages)
                {
                    msg.DownloadedToPc = true;
                    try
                    {
                        string safeSubj = string.Concat((msg.Subject ?? "mail").Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
                        if (safeSubj.Length > 50) safeSubj = safeSubj[..50];
                        string emlOut = Path.Combine(targetDir, $"{msg.DateUtc:yyyyMMdd_HHmmss}_{safeSubj}_{msg.Id[..6]}.eml");

                        if (!string.IsNullOrWhiteSpace(msg.LocalEmlEncryptedPath) && File.Exists(msg.LocalEmlEncryptedPath))
                        {
                            byte[] rawBytes = MailCryptoService.DecryptFromFile(msg.LocalEmlEncryptedPath);
                            File.WriteAllBytes(emlOut, rawBytes);
                        }
                        else
                        {
                            File.WriteAllText(emlOut, $"From: {msg.SenderDisplay}\r\nTo: {msg.ToDisplay}\r\nSubject: {msg.Subject}\r\nDate: {msg.DateUtc:R}\r\n\r\n{msg.BodyText}", Encoding.UTF8);
                        }
                        count++;
                    }
                    catch (Exception ex)
                    {
                        Settings.Log(ex);
                    }
                }
            }
            SaveIndex();
        }
        MessagesUpdated?.Invoke();
        return (count, finalPath);
    }

    public List<MailMessageItem> GetAccountMessages(string accountId)
    {
        lock (gate)
        {
            return messages.Where(m => m.AccountId == accountId).ToList();
        }
    }

    public byte[] GetMessageRawBytes(MailMessageItem msg)
    {
        if (!string.IsNullOrWhiteSpace(msg.LocalEmlEncryptedPath) && File.Exists(msg.LocalEmlEncryptedPath))
        {
            try
            {
                return MailCryptoService.DecryptFromFile(msg.LocalEmlEncryptedPath);
            }
            catch { }
        }
        string content = $"From: {msg.SenderDisplay}\r\nTo: {msg.ToDisplay}\r\nSubject: {msg.Subject}\r\nDate: {msg.DateUtc:R}\r\nMessage-ID: <{msg.Id}@pixelhelper.local>\r\n\r\n{msg.BodyText}";
        return Encoding.UTF8.GetBytes(content);
    }

    public async Task<(int OffloadedCount, string DestinationPath)> OffloadAndPurgeAccountAsync(
        MailAccountConfig account,
        CancellationToken token = default)
    {
        var syncGate = accountSyncGates.GetOrAdd(account.Id, _ => new SemaphoreSlim(1, 1));
        await syncGate.WaitAsync(token);
        try
        {
            string plainPassword = MailCryptoService.UnprotectSecret(account.EncryptedPassword);
            var purged = await MailProtocolClient.FetchAndPurgeAllAsync(account, plainPassword, token);
            foreach (var (uid, rawMime, folder) in purged)
            {
                var item = await IngestRawMimeAsync(account, uid, rawMime, folder, token);
                item.DownloadedToPc = true;
            }

            var (count, destPath) = OffloadAccountToComputer(account.Id, format: account.OffloadFormat, accountEmail: account.EmailAddress);

            account.LastSyncUtc = DateTime.UtcNow;
            account.LastSyncStatus = $"Выгружено на ПК и удалено с сервера: {purged.Count} писем ({DateTime.Now:HH:mm:ss})";
            settings.Save();

            MessagesUpdated?.Invoke();
            return (purged.Count, destPath);
        }
        finally
        {
            syncGate.Release();
        }
    }

    public static MailMessageItem LoadSingleEml(string emlFilePath)
    {
        if (!File.Exists(emlFilePath)) throw new FileNotFoundException("Файл не найден", emlFilePath);
        byte[] rawBytes = File.ReadAllBytes(emlFilePath);
        var parsed = MailProtocolClient.ParseMimeMessage(rawBytes, "local_file", "local_file", Path.GetFileName(emlFilePath));
        var msg = parsed.Message;
        msg.Folder = MailFolderKind.LocalArchive;
        msg.IsRead = true;
        msg.DownloadedToPc = true;
        return msg;
    }

    public static List<MailMessageItem> LoadMbox(string mboxFilePath)
    {
        if (!File.Exists(mboxFilePath)) throw new FileNotFoundException("Файл архива не найден", mboxFilePath);
        var items = new List<MailMessageItem>();
        using var fs = File.OpenRead(mboxFilePath);
        using var reader = new StreamReader(fs, Encoding.UTF8);

        var sb = new StringBuilder();
        string? line;
        int index = 0;

        void FlushCurrent()
        {
            if (sb.Length > 0)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
                try
                {
                    var parsed = MailProtocolClient.ParseMimeMessage(bytes, "mbox_archive", Path.GetFileName(mboxFilePath), $"mbox_{index++}");
                    parsed.Message.Folder = MailFolderKind.LocalArchive;
                    parsed.Message.IsRead = true;
                    parsed.Message.DownloadedToPc = true;
                    items.Add(parsed.Message);
                }
                catch (Exception ex)
                {
                    Settings.Log(ex);
                }
                sb.Clear();
            }
        }

        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("From ") && (line.Length > 5 && line.IndexOf('@') > 0 || line.Contains("MAILER-DAEMON")))
            {
                FlushCurrent();
                continue;
            }

            if (line.StartsWith(">From "))
                line = line[1..];

            sb.AppendLine(line);
        }
        FlushCurrent();

        return items;
    }

    public MailMessageItem? GetMessageById(string messageId)
    {
        lock (gate)
        {
            return messages.FirstOrDefault(m => m.Id == messageId);
        }
    }

    public void MarkAsRead(string messageId, bool isRead = true)
    {
        lock (gate)
        {
            var msg = messages.FirstOrDefault(m => m.Id == messageId);
            if (msg != null && msg.IsRead != isRead)
            {
                msg.IsRead = isRead;
                SaveIndex();
            }
        }
        MessagesUpdated?.Invoke();
    }

    public void MoveToFolder(string messageId, MailFolderKind targetFolder)
    {
        lock (gate)
        {
            var msg = messages.FirstOrDefault(m => m.Id == messageId);
            if (msg != null)
            {
                msg.Folder = targetFolder;
                SaveIndex();
            }
        }
        MessagesUpdated?.Invoke();
    }

    public void DeleteMessage(string messageId)
    {
        lock (gate)
        {
            var msg = messages.FirstOrDefault(m => m.Id == messageId);
            if (msg == null) return;

            if (msg.Folder != MailFolderKind.Trash)
            {
                msg.Folder = MailFolderKind.Trash;
            }
            else
            {
                try
                {
                    if (!string.IsNullOrEmpty(msg.LocalEmlEncryptedPath) && File.Exists(msg.LocalEmlEncryptedPath))
                        File.Delete(msg.LocalEmlEncryptedPath);
                    foreach (var att in msg.Attachments)
                    {
                        if (!string.IsNullOrEmpty(att.LocalEncryptedPath) && File.Exists(att.LocalEncryptedPath))
                            File.Delete(att.LocalEncryptedPath);
                    }
                }
                catch { }
                messages.Remove(msg);
            }
            SaveIndex();
        }
        MessagesUpdated?.Invoke();
    }

    public void ApplyFilterListsToExistingMessages()
    {
        lock (gate)
        {
            foreach (var msg in messages)
            {
                if (msg.SecurityVerdict == MailSecurityVerdict.BlockedMalware) continue;

                if (MailSecurityScanner.IsSenderBlocked(msg.FromAddress, settings))
                {
                    msg.SecurityVerdict = MailSecurityVerdict.BlockedBlacklist;
                    msg.SecurityReason = $"🚫 Адрес отправителя ({msg.FromAddress}) находится в чёрном списке";
                    if (msg.Folder == MailFolderKind.Inbox) msg.Folder = MailFolderKind.Spam;
                }
                else if (MailSecurityScanner.IsSenderTrusted(msg.FromAddress, settings))
                {
                    msg.SecurityVerdict = MailSecurityVerdict.TrustedWhitelist;
                    msg.SecurityReason = $"✅ Доверенный отправитель из белого списка ({msg.FromAddress})";
                    if (msg.Folder == MailFolderKind.Spam) msg.Folder = MailFolderKind.Inbox;
                }
            }
            SaveIndex();
        }
        MessagesUpdated?.Invoke();
    }

    public byte[]? ReadAttachmentDecrypted(MailAttachmentItem attachment)
    {
        if (attachment.IsThreat) return null;
        if (string.IsNullOrWhiteSpace(attachment.LocalEncryptedPath) || !File.Exists(attachment.LocalEncryptedPath))
            return null;
        return MailCryptoService.DecryptFromFile(attachment.LocalEncryptedPath);
    }

    public async Task<MailMessageItem> SaveOrSendOutgoingAsync(
        MailAccountConfig account,
        MailMessageItem draft,
        List<ParsedAttachmentData> attachments,
        bool sendNow,
        string? encryptionKey,
        CancellationToken token = default)
    {
        draft.AccountId = account.Id;
        draft.AccountEmail = account.EmailAddress;
        draft.FromName = account.DisplayName;
        draft.FromAddress = account.EmailAddress;
        draft.DateUtc = DateTime.UtcNow;
        draft.IsRead = true;

        byte[] rawMime = MailProtocolClient.BuildRawMime(account, draft, attachments, encryptionKey);

        if (sendNow)
        {
            string plainPassword = MailCryptoService.UnprotectSecret(account.EncryptedPassword);
            await MailProtocolClient.SendSmtpAsync(account, plainPassword, draft, attachments, encryptionKey, token);
            draft.Folder = MailFolderKind.Sent;
        }
        else
        {
            draft.Folder = MailFolderKind.Drafts;
        }

        string accountDir = Path.Combine(StoreRoot, account.Id, draft.Folder.ToString());
        Directory.CreateDirectory(accountDir);
        string emlPath = Path.Combine(accountDir, draft.Id + ".eml.enc");
        MailCryptoService.EncryptToFile(rawMime, emlPath);
        draft.LocalEmlEncryptedPath = emlPath;
        draft.DownloadedToPc = true;

        string attDir = Path.Combine(StoreRoot, account.Id, "Attachments", draft.Id);
        draft.Attachments.Clear();
        foreach (var att in attachments)
        {
            Directory.CreateDirectory(attDir);
            var attItem = new MailAttachmentItem
            {
                FileName = att.FileName,
                ContentType = att.ContentType,
                SizeBytes = att.Content.Length
            };
            string attPath = Path.Combine(attDir, attItem.Id + ".att.enc");
            MailCryptoService.EncryptToFile(att.Content, attPath);
            attItem.LocalEncryptedPath = attPath;
            draft.Attachments.Add(attItem);
        }

        lock (gate)
        {
            messages.RemoveAll(m => m.Id == draft.Id);
            messages.Add(draft);
            SaveIndex();
        }

        MessagesUpdated?.Invoke();
        return draft;
    }
}

