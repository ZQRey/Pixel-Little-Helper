using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

namespace LitleHelperServer;

public static class Roles
{
    public const string SuperAdmin = "SuperAdmin", Admin = "Admin", Operator = "Operator", Psychologist = "Psychologist", User = "User";
    public static readonly string[] All = [SuperAdmin, Admin, Operator, Psychologist, User];
}
public class Computer
{
    public int Id { get; set; }
    public string MachineName { get; set; } = "";
    public string DomainName { get; set; } = "";
    public string CurrentUser { get; set; } = "";
    public string CurrentUserFullName { get; set; } = "";
    public bool SupportsSystemButtons { get; set; }
    public bool SupportsAiDiagnostics { get; set; }
    public string IpAddress { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public bool IsOnline { get; set; }
    public DateTime LastSeen { get; set; }
    public string HardwareJson { get; set; } = "{}";
    public string SoftwareJson { get; set; } = "[]";
    public int? BranchId { get; set; }
    public string Room { get; set; } = "";
    [JsonIgnore] public string? ConnectionId { get; set; }
    [JsonIgnore] public string AdMachineObjectId { get; set; } = "";
    [JsonIgnore] public string AgentKeyHash { get; set; } = "";
}
public class ActionButton
{
    public string? SystemKey { get; set; }
    public bool IsSystem { get; set; }
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string IconName { get; set; } = "";
    public string ActionType { get; set; } = "open_url";
    public string Payload { get; set; } = "";
    public int OrderIndex { get; set; }
    public bool IsActive { get; set; } = true;
    public string TargetGroup { get; set; } = "All";
    public string Description { get; set; } = "";

    [JsonIgnore]
    public string TitleTranslationsJson { get; set; } = "{}";
    [JsonIgnore]
    public string DescriptionTranslationsJson { get; set; } = "{}";

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public Dictionary<string, string> TitleTranslations
    {
        get
        {
            try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(TitleTranslationsJson) ?? new(StringComparer.OrdinalIgnoreCase); }
            catch { return new(StringComparer.OrdinalIgnoreCase); }
        }
        set
        {
            TitleTranslationsJson = System.Text.Json.JsonSerializer.Serialize(value ?? new Dictionary<string, string>());
        }
    }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public Dictionary<string, string> DescriptionTranslations
    {
        get
        {
            try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(DescriptionTranslationsJson) ?? new(StringComparer.OrdinalIgnoreCase); }
            catch { return new(StringComparer.OrdinalIgnoreCase); }
        }
        set
        {
            DescriptionTranslationsJson = System.Text.Json.JsonSerializer.Serialize(value ?? new Dictionary<string, string>());
        }
    }
}
public class PanelUser
{
    [JsonIgnore] public string AdObjectId { get; set; } = "";
    [System.ComponentModel.DataAnnotations.Schema.NotMapped] public string AuthSource => PasswordHash == "!AD" ? "AD" : "Local";
    public int Id { get; set; }
    public string Username { get; set; } = "";
    [JsonIgnore] public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Role { get; set; } = Roles.User;
    public string AssistantMachine { get; set; } = "";
    public int? BranchId { get; set; }
    [JsonIgnore] public string BranchIdsJson { get; set; } = "[]";
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    [System.Text.Json.Serialization.JsonPropertyName("branchIds")]
    public int[] BranchIds
    {
        get
        {
            try
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<int>>(string.IsNullOrWhiteSpace(BranchIdsJson) ? "[]" : BranchIdsJson) ?? new();
                if (list.Count == 0 && BranchId.HasValue) list.Add(BranchId.Value);
                return list.Distinct().ToArray();
            }
            catch
            {
                return BranchId.HasValue ? new[] { BranchId.Value } : Array.Empty<int>();
            }
        }
        set
        {
            var list = (value ?? Array.Empty<int>()).Distinct().ToList();
            BranchIdsJson = System.Text.Json.JsonSerializer.Serialize(list);
            BranchId = list.Count > 0 ? list[0] : null;
        }
    }
    public string Room { get; set; } = "";
    [JsonIgnore] public string PermissionOverrides { get; set; } = "{}";
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public Dictionary<string, bool> Permissions
    {
        get => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, bool>>(PermissionOverrides)!;
        set => PermissionOverrides = System.Text.Json.JsonSerializer.Serialize(value);
    }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped] public string[] EffectivePermissions => Access.Effective(this);
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool MustChangePassword { get; set; }
    [JsonIgnore] public int SecurityVersion { get; set; }
}
public class AuditLog
{
    public int Id { get; set; }
    public string TaskId { get; set; } = Guid.NewGuid().ToString("N");
    public string AdminUsername { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string CommandType { get; set; } = "";
    public string CommandPayload { get; set; } = "";
    public string Result { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int? ExitCode { get; set; }
}
public class TicketRecord
{
    public int Id { get; set; }
    public int GlpiId { get; set; }
    public string Username { get; set; } = "";
    public string UserFullName { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int? BranchId { get; set; }
    public string BranchName { get; set; } = "";
    public string Room { get; set; } = "";
    public string Status { get; set; } = "1";
    public int AssignedGlpiUserId { get; set; }
    public string AssignedUsername { get; set; } = "";
    public DateTime? SyncedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class KnowledgeArticle
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Category { get; set; } = "General";
    public string ContentMarkdown { get; set; } = "";
    public string Keywords { get; set; } = "";
    public int? BranchId { get; set; }
    public string TargetRole { get; set; } = "All";
    public int ViewCount { get; set; }
    public bool IsPinned { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
public class ChatPoll
{
    public int Id { get; set; }
    public int Peer { get; set; }
    public int CreatorId { get; set; }
    public string Question { get; set; } = "";
    public string OptionsJson { get; set; } = "[]";
    public bool MultipleChoice { get; set; }
    public bool IsAnonymous { get; set; }
    public bool IsClosed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class ChatPollVote
{
    public int Id { get; set; }
    public int PollId { get; set; }
    public int UserId { get; set; }
    public int OptionIndex { get; set; }
    public DateTime VotedAt { get; set; } = DateTime.UtcNow;
}
public class ThreatRecord
{
    public int Id { get; set; }
    public string MachineName { get; set; } = "";
    public string UserName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string ThreatName { get; set; } = "";
    public string ThreatType { get; set; } = "ClamAV"; // Hash, Yara, ClamAV, Heuristic
    public long FileSizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string Status { get; set; } = "Quarantined"; // Quarantined, Restored, Deleted
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? QuarantinedUntil { get; set; }
    public string ActionTaken { get; set; } = "Quarantined";
    public DateTime? RestoredAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
public class AntivirusWhitelistItem
{
    public int Id { get; set; }
    public string Sha256 { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string AddedBy { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
public class HelperDb(DbContextOptions<HelperDb> options) : DbContext(options)
{
    public DbSet<AiSession> AiSessions => Set<AiSession>();
    public DbSet<AiMessage> AiMessages => Set<AiMessage>();
    public DbSet<AiDiagnosticCheck> AiDiagnosticChecks => Set<AiDiagnosticCheck>();
    public DbSet<AiRequestLog> AiRequestLogs => Set<AiRequestLog>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatFile> ChatFiles => Set<ChatFile>();
    public DbSet<ChatGroupRestriction> ChatGroupRestrictions => Set<ChatGroupRestriction>();
    public DbSet<ChatBroadcast> ChatBroadcasts => Set<ChatBroadcast>();
    public DbSet<ChatPreference> ChatPreferences => Set<ChatPreference>();
    public DbSet<ChatReaction> ChatReactions => Set<ChatReaction>();
    public DbSet<ChatPoll> ChatPolls => Set<ChatPoll>();
    public DbSet<ChatPollVote> ChatPollVotes => Set<ChatPollVote>();
    public DbSet<ChatGroup> ChatGroups => Set<ChatGroup>();
    public DbSet<ChatGroupEvent> ChatGroupEvents => Set<ChatGroupEvent>();
    public DbSet<ChatGroupDeparture> ChatGroupDepartures => Set<ChatGroupDeparture>();
    public DbSet<ChatGroupMember> ChatGroupMembers => Set<ChatGroupMember>();
    public DbSet<ChatGroupMessage> ChatGroupMessages => Set<ChatGroupMessage>();
    public DbSet<TelegramBotState> TelegramBotStates => Set<TelegramBotState>();
    public DbSet<TelegramReplySession> TelegramReplySessions => Set<TelegramReplySession>();
    public DbSet<TelegramHandledUpdate> TelegramHandledUpdates => Set<TelegramHandledUpdate>();
    public DbSet<TelegramDelivery> TelegramDeliveries => Set<TelegramDelivery>();
    public DbSet<EmergencyEvent> EmergencyEvents => Set<EmergencyEvent>();
    public DbSet<Computer> Computers => Set<Computer>();
    public DbSet<MedicalStatusConfig> MedicalStatuses => Set<MedicalStatusConfig>();
    public DbSet<ActionButton> Buttons => Set<ActionButton>();
    public DbSet<PanelUser> Users => Set<PanelUser>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<TicketRecord> Tickets => Set<TicketRecord>();
    public DbSet<TicketAttachment> TicketAttachments => Set<TicketAttachment>();
    public DbSet<SuperAdminButton> SuperAdminButtons => Set<SuperAdminButton>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<KnowledgeArticle> KnowledgeArticles => Set<KnowledgeArticle>();
    public DbSet<GlpiReportState> GlpiReportStates => Set<GlpiReportState>();
    public DbSet<TicketSurveyInvitation> TicketSurveyInvitations => Set<TicketSurveyInvitation>();
    public DbSet<SurveyAdminAlert> SurveyAdminAlerts => Set<SurveyAdminAlert>();
    public DbSet<ThreatRecord> ThreatRecords => Set<ThreatRecord>();
    public DbSet<AntivirusWhitelistItem> AntivirusWhitelist => Set<AntivirusWhitelistItem>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AiSession>().HasIndex(s => new { s.OwnerUserId, s.WindowInstanceId }).IsUnique();
        b.Entity<AiMessage>().HasIndex(m => new { m.SessionId, m.ClientMessageId }).IsUnique();
        b.Entity<AiMessage>().HasIndex(m => new { m.SessionId, m.Sequence }).IsUnique();
        b.Entity<AiMessage>().HasOne<AiSession>().WithMany().HasForeignKey(m => m.SessionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<AiDiagnosticCheck>().HasIndex(m => new { m.SessionId, m.ClientRequestId }).IsUnique();
        b.Entity<AiDiagnosticCheck>().HasOne<AiSession>().WithMany().HasForeignKey(m => m.SessionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<AiRequestLog>().HasIndex(m => new { m.OwnerUserId, m.CreatedAt });
        b.Entity<ChatGroupMember>().HasKey(m => new { m.GroupId, m.UserId });
        b.Entity<ChatGroupDeparture>().HasKey(m => new { m.GroupId, m.UserId });
        b.Entity<ChatGroup>().HasOne<PanelUser>().WithMany().HasForeignKey(g => g.OwnerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ChatGroupMember>().HasOne<ChatGroup>().WithMany().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ChatGroupMember>().HasOne<PanelUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ChatGroupMessage>().HasOne<ChatGroup>().WithMany().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ChatGroupMessage>().HasOne<PanelUser>().WithMany().HasForeignKey(m => m.SenderId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ChatGroupMember>().HasIndex(m => m.UserId);
        b.Entity<ChatGroupMessage>().HasIndex(m => new { m.SenderId, m.ClientId }).IsUnique();
        b.Entity<ChatGroupMessage>().HasIndex(m => new { m.GroupId, m.Id });
        b.Entity<ChatMessage>().HasIndex(m => new { m.SenderId, m.ClientId }).IsUnique();
        b.Entity<ChatMessage>().HasIndex(m => new { m.RecipientId, m.Id });
        b.Entity<TelegramDelivery>().HasKey(x => x.TicketId);
        b.Entity<TelegramDelivery>().HasOne(x => x.Ticket).WithOne().HasForeignKey<TelegramDelivery>(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<MedicalStatusConfig>().HasIndex(x => x.Key).IsUnique();
        b.Entity<ActionButton>().HasIndex(x => x.SystemKey).IsUnique();
        b.Entity<Computer>().HasIndex(x => x.MachineName).IsUnique();
        b.Entity<PanelUser>().HasIndex(x => x.Username).IsUnique();
        b.Entity<PanelUser>().HasIndex(x => x.AdObjectId).IsUnique().HasFilter("\"AdObjectId\" <> ''");
        b.Entity<PanelUser>().Property(x => x.BranchIdsJson).HasColumnName("BranchIds").HasDefaultValue("[]");
        b.Entity<AuditLog>().HasIndex(x => x.TaskId).IsUnique();
        b.Entity<TicketRecord>().HasIndex(x => x.Username);
        b.Entity<TicketAttachment>().HasIndex(x => x.TicketId);
        b.Entity<KnowledgeArticle>().HasIndex(x => x.Category);
        b.Entity<ThreatRecord>().HasIndex(x => x.MachineName);
        b.Entity<ThreatRecord>().HasIndex(x => x.Status);
        b.Entity<ThreatRecord>().HasIndex(x => x.DetectedAt);
        b.Entity<AntivirusWhitelistItem>().HasIndex(x => x.Sha256);
    }
}
public record MachineInfo(string MachineName, string UserName, string DomainName, string IpAddress, string OsVersion, string UserFullName = "", bool SupportsSystemButtons = false, bool SupportsAiDiagnostics = false);
public record CommandEnvelope(string TaskId, string Type, string Payload);
public record LoginRequest(string Username, string Password);
public record PasswordRequest(string CurrentPassword, string NewPassword);
public record UserRequest(string Username, string FullName, string Role, bool IsActive, string? Password, Dictionary<string, bool>? Permissions = null, string? AssistantMachine = null, int? BranchId = null, string? Room = null, int[]? BranchIds = null);
public record CommandRequest(string[] Machines, string Type, string Payload);
public record EnrollmentRequest(string MachineName);
public record AgentRegistrationRequest(string MachineName, string ClientKey);
public record TicketRequest(string Title, string Description, int? BranchId = null, string Room = "", string UserFullName = "");
public record CartridgeReadyNotice(string Username, string Marker, string Model, string Cabinet, string ItOffice, string Message);
public record TicketReplyNotice(int GlpiId, string Title, string Author, string Text);
public record EmergencyAlertNotice(string Code, string Title, string? Cabinet, string? Notes, string? Subcode, string? ImageBase64, string? ImageUrl, int DurationSeconds, int? CallId, string? Department, List<string>? Languages = null, string? EventId = null);
public record CartridgeReadyRequest(string Username, string Marker, string Model, string Cabinet, string ItOffice, string Message);
public record EmergencyAlertRequest(string Code, string? Title, string? Cabinet, string? Notes, string? Subcode, string? ImageBase64, string? ImageUrl, int? DurationSeconds, int? CallId, string? Department, List<string>? Languages = null, int? BranchId = null, string? BranchName = null);
public record EmergencyPingRequest(string? ServerUrl, string? ApiKey);
public record KnowledgeArticleRequest(string Title, string Category, string ContentMarkdown, string Keywords = "", int? BranchId = null, string TargetRole = "All", bool IsPinned = false);
public record HelperWhiteTriggerRequest(string? Cabinet = null, string? BranchName = null, string? Time = null, string? EventId = null);
public record BranchSyncRequest(string? Action = null, string? Name = null, string? ComputerPrefixes = null, string? IpSubnets = null, bool? IsActive = null, Branch? Branch = null);


