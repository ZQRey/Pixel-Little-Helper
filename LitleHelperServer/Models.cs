using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

namespace LitleHelperServer;

public static class Roles
{
    public const string SuperAdmin = "SuperAdmin", Admin = "Admin", Operator = "Operator", User = "User";
    public static readonly string[] All = [SuperAdmin, Admin, Operator, User];
}
public class Computer
{
    public int Id { get; set; }
    public string MachineName { get; set; } = "";
    public string DomainName { get; set; } = "";
    public string CurrentUser { get; set; } = "";
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
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string IconName { get; set; } = "";
    public string ActionType { get; set; } = "open_url";
    public string Payload { get; set; } = "";
    public int OrderIndex { get; set; }
    public bool IsActive { get; set; } = true;
    public string TargetGroup { get; set; } = "All";
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
public class HelperDb(DbContextOptions<HelperDb> options) : DbContext(options)
{
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatFile> ChatFiles => Set<ChatFile>();
    public DbSet<ChatGroupRestriction> ChatGroupRestrictions => Set<ChatGroupRestriction>();
    public DbSet<ChatBroadcast> ChatBroadcasts => Set<ChatBroadcast>();
    public DbSet<ChatPreference> ChatPreferences => Set<ChatPreference>();
    public DbSet<ChatReaction> ChatReactions => Set<ChatReaction>();
    public DbSet<ChatGroup> ChatGroups => Set<ChatGroup>();
    public DbSet<ChatGroupEvent> ChatGroupEvents => Set<ChatGroupEvent>();
    public DbSet<ChatGroupDeparture> ChatGroupDepartures => Set<ChatGroupDeparture>();
    public DbSet<ChatGroupMember> ChatGroupMembers => Set<ChatGroupMember>();
    public DbSet<ChatGroupMessage> ChatGroupMessages => Set<ChatGroupMessage>();
    public DbSet<TelegramBotState> TelegramBotStates => Set<TelegramBotState>();
    public DbSet<TelegramReplySession> TelegramReplySessions => Set<TelegramReplySession>();
    public DbSet<TelegramHandledUpdate> TelegramHandledUpdates => Set<TelegramHandledUpdate>();
    public DbSet<TelegramDelivery> TelegramDeliveries => Set<TelegramDelivery>();
    public DbSet<Computer> Computers => Set<Computer>();
    public DbSet<ActionButton> Buttons => Set<ActionButton>();
    public DbSet<PanelUser> Users => Set<PanelUser>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<TicketRecord> Tickets => Set<TicketRecord>();
    public DbSet<SuperAdminButton> SuperAdminButtons => Set<SuperAdminButton>();
    public DbSet<Branch> Branches => Set<Branch>();
    protected override void OnModelCreating(ModelBuilder b)
    {
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
        b.Entity<Computer>().HasIndex(x => x.MachineName).IsUnique();
        b.Entity<PanelUser>().HasIndex(x => x.Username).IsUnique();
        b.Entity<PanelUser>().HasIndex(x => x.AdObjectId).IsUnique().HasFilter("\"AdObjectId\" <> ''");
        b.Entity<AuditLog>().HasIndex(x => x.TaskId).IsUnique();
        b.Entity<TicketRecord>().HasIndex(x => x.Username);
    }
}
public record MachineInfo(string MachineName, string UserName, string DomainName, string IpAddress, string OsVersion);
public record CommandEnvelope(string TaskId, string Type, string Payload);
public record LoginRequest(string Username, string Password);
public record PasswordRequest(string CurrentPassword, string NewPassword);
public record UserRequest(string Username, string FullName, string Role, bool IsActive, string? Password, Dictionary<string, bool>? Permissions = null, string? AssistantMachine = null, int? BranchId = null, string? Room = null);
public record CommandRequest(string[] Machines, string Type, string Payload);
public record EnrollmentRequest(string MachineName);
public record AgentRegistrationRequest(string MachineName, string ClientKey);
public record TicketRequest(string Title, string Description, int? BranchId = null, string Room = "");
public record CartridgeReadyNotice(string Username, string Marker, string Model, string Cabinet, string ItOffice, string Message);
public record TicketReplyNotice(int GlpiId, string Title, string Author, string Text);
public record EmergencyAlertNotice(string Code, string Title, string? Cabinet, string? Notes, string? Subcode, string? ImageBase64, string? ImageUrl, int DurationSeconds, int? CallId, string? Department);
public record CartridgeReadyRequest(string Username, string Marker, string Model, string Cabinet, string ItOffice, string Message);
public record EmergencyAlertRequest(string Code, string? Title, string? Cabinet, string? Notes, string? Subcode, string? ImageBase64, string? ImageUrl, int? DurationSeconds, int? CallId, string? Department);

