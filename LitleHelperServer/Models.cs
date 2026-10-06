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
    [JsonIgnore] public string? ConnectionId { get; set; }
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
    [System.ComponentModel.DataAnnotations.Schema.NotMapped] public string AuthSource => PasswordHash == "!AD" ? "AD" : "Local";
    public int Id { get; set; }
    public string Username { get; set; } = "";
    [JsonIgnore] public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Role { get; set; } = Roles.User;
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
    public string Status { get; set; } = "1";
    public int AssignedGlpiUserId { get; set; }
    public string AssignedUsername { get; set; } = "";
    public DateTime? SyncedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class HelperDb(DbContextOptions<HelperDb> options) : DbContext(options)
{
    public DbSet<TelegramBotState> TelegramBotStates => Set<TelegramBotState>();
    public DbSet<TelegramReplySession> TelegramReplySessions => Set<TelegramReplySession>();
    public DbSet<TelegramHandledUpdate> TelegramHandledUpdates => Set<TelegramHandledUpdate>();
    public DbSet<TelegramDelivery> TelegramDeliveries => Set<TelegramDelivery>();
    public DbSet<Computer> Computers => Set<Computer>();
    public DbSet<ActionButton> Buttons => Set<ActionButton>();
    public DbSet<PanelUser> Users => Set<PanelUser>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<TicketRecord> Tickets => Set<TicketRecord>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<TelegramDelivery>().HasKey(x => x.TicketId);
        b.Entity<TelegramDelivery>().HasOne(x => x.Ticket).WithOne().HasForeignKey<TelegramDelivery>(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Computer>().HasIndex(x => x.MachineName).IsUnique();
        b.Entity<PanelUser>().HasIndex(x => x.Username).IsUnique();
        b.Entity<AuditLog>().HasIndex(x => x.TaskId).IsUnique();
        b.Entity<TicketRecord>().HasIndex(x => x.Username);
    }
}
public record MachineInfo(string MachineName, string UserName, string DomainName, string IpAddress, string OsVersion);
public record CommandEnvelope(string TaskId, string Type, string Payload);
public record LoginRequest(string Username, string Password);
public record PasswordRequest(string CurrentPassword, string NewPassword);
public record UserRequest(string Username, string FullName, string Role, bool IsActive, string? Password, Dictionary<string, bool>? Permissions = null);
public record CommandRequest(string[] Machines, string Type, string Payload);
public record EnrollmentRequest(string MachineName);
public record AgentRegistrationRequest(string MachineName, string ClientKey);
public record TicketRequest(string Title, string Description);
