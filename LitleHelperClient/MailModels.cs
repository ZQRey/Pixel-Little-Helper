using System.Text.Json.Serialization;

namespace PixelHelper;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MailIncomingProtocol
{
    Imap,
    Pop3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MailSslMode
{
    ImplicitSsl,
    StartTls,
    None
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MailStorageMode
{
    KeepOnServer,
    DownloadAllToComputer
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MailEncryptionMode
{
    None,
    PixelAes256,
    SMime
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MailSecurityVerdict
{
    Clean,
    TrustedWhitelist,
    Spam,
    PhishingWarning,
    BlockedBlacklist,
    BlockedMalware
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MailFolderKind
{
    UnifiedInbox,
    Inbox,
    Sent,
    Drafts,
    Spam,
    Trash,
    LocalArchive
}

public enum MailOffloadFormat
{
    IndividualEml = 0,
    SingleFileMbox = 1
}

public sealed class MailAccountConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string EmailAddress { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Username { get; set; } = "";
    public string EncryptedPassword { get; set; } = "";

    public MailIncomingProtocol IncomingProtocol { get; set; } = MailIncomingProtocol.Imap;
    public string IncomingHost { get; set; } = "";
    public int IncomingPort { get; set; } = 993;
    public MailSslMode IncomingSslMode { get; set; } = MailSslMode.ImplicitSsl;

    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 465;
    public MailSslMode SmtpSslMode { get; set; } = MailSslMode.ImplicitSsl;

    public MailStorageMode StorageMode { get; set; } = MailStorageMode.KeepOnServer;
    public bool DeleteFromServerAfterDownload { get; set; }
    public MailOffloadFormat OffloadFormat { get; set; } = MailOffloadFormat.IndividualEml;
    public int CheckIntervalMinutes { get; set; } = 3;

    public MailEncryptionMode EncryptionMode { get; set; } = MailEncryptionMode.None;
    public string SMimeCertificateThumbprint { get; set; } = "";

    public bool IsEnabled { get; set; } = true;
    public bool IsDefault { get; set; }
    public DateTime? LastSyncUtc { get; set; }
    public string LastSyncStatus { get; set; } = "Не синхронизирован";
}

public sealed class MailAttachmentItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string LocalEncryptedPath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public bool IsThreat { get; set; }
    public string ThreatName { get; set; } = "";
    public string ThreatType { get; set; } = "";
    public string QuarantineId { get; set; } = "";
}

public sealed class MailMessageItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AccountId { get; set; } = "";
    public string AccountEmail { get; set; } = "";
    public string RemoteUid { get; set; } = "";
    public MailFolderKind Folder { get; set; } = MailFolderKind.Inbox;

    public string Subject { get; set; } = "(Без темы)";
    public string FromName { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string ReplyToAddress { get; set; } = "";
    public List<string> ToAddresses { get; set; } = new();
    public List<string> CcAddresses { get; set; } = new();

    public DateTime DateUtc { get; set; } = DateTime.UtcNow;
    public string Snippet { get; set; } = "";
    public string BodyText { get; set; } = "";
    public string BodyHtml { get; set; } = "";
    public string HeadersRaw { get; set; } = "";

    public bool IsRead { get; set; }
    public bool IsStarred { get; set; }
    public bool IsEncrypted { get; set; }
    public MailEncryptionMode EncryptionType { get; set; } = MailEncryptionMode.None;
    public bool SignatureVerified { get; set; }

    public MailSecurityVerdict SecurityVerdict { get; set; } = MailSecurityVerdict.Clean;
    public string SecurityReason { get; set; } = "Проверено PixelGuard AV: угроз не обнаружено";
    public int SpamScore { get; set; }
    public List<string> PhishingWarnings { get; set; } = new();

    public List<MailAttachmentItem> Attachments { get; set; } = new();
    public string LocalEmlEncryptedPath { get; set; } = "";
    public bool DownloadedToPc { get; set; }

    [JsonIgnore]
    public bool HasAttachments => Attachments.Count > 0;

    [JsonIgnore]
    public string SenderDisplay =>
        !string.IsNullOrWhiteSpace(FromName) && !string.Equals(FromName, FromAddress, StringComparison.OrdinalIgnoreCase)
            ? $"{FromName} <{FromAddress}>"
            : FromAddress;

    [JsonIgnore]
    public string ToDisplay => ToAddresses.Count > 0 ? string.Join(", ", ToAddresses) : AccountEmail;
}

