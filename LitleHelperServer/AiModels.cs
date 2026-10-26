using Microsoft.EntityFrameworkCore;

namespace LitleHelperServer;

public sealed class AiSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int OwnerUserId { get; set; }
    public int SecurityVersion { get; set; }
    public string WindowInstanceId { get; set; } = "";
    public string Backend { get; set; } = "";
    public string Channel { get; set; } = "chat";
    public string ModelId { get; set; } = "";
    public string SettingsVersion { get; set; } = "";
    public string ExternalSessionId { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string ProtectedOptions { get; set; } = "";
    public string Status { get; set; } = "Active";
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public long UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
public sealed class AiMessage
{
    public long Sequence { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = "";
    public string ClientMessageId { get; set; } = "";
    public string Role { get; set; } = "user";
    public string Text { get; set; } = "";
    public string Status { get; set; } = "Running";
    public string Reply { get; set; } = "";
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public string SourcesJson { get; set; } = "[]";
    public string UsageJson { get; set; } = "{}";
    public long DurationMs { get; set; }
}
public sealed class AiDiagnosticCheck
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = "";
    public int OwnerUserId { get; set; }
    public string ClientRequestId { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string Tool { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string Status { get; set; } = "Running";
    public string ResultJson { get; set; } = "{}";
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
// No FK to conversations: removing chat content must not reset request quotas or audit metadata.
public sealed class AiRequestLog
{
    public string Id { get; set; } = "";
    public int OwnerUserId { get; set; }
    public string SessionId { get; set; } = "";
    public string Backend { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string Status { get; set; } = "Running";
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public long DurationMs { get; set; }
    public string UsageJson { get; set; } = "{}";
}
public static class AiSchema
{
    public static async Task EnsureAsync(HelperDb db)
    {
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS \"AiSessions\" (\"Id\" TEXT PRIMARY KEY, \"OwnerUserId\" INTEGER NOT NULL, \"WindowInstanceId\" TEXT NOT NULL, \"Backend\" TEXT NOT NULL, \"ModelId\" TEXT NOT NULL, \"SettingsVersion\" TEXT NOT NULL, \"ExternalSessionId\" TEXT NOT NULL, \"Status\" TEXT NOT NULL, \"CreatedAt\" BIGINT NOT NULL, \"UpdatedAt\" BIGINT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_AiSessions_OwnerUserId_WindowInstanceId\" ON \"AiSessions\" (\"OwnerUserId\", \"WindowInstanceId\")");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS \"AiMessages\" (\"Id\" TEXT PRIMARY KEY, \"SessionId\" TEXT NOT NULL REFERENCES \"AiSessions\"(\"Id\") ON DELETE CASCADE, \"ClientMessageId\" TEXT NOT NULL, \"Role\" TEXT NOT NULL, \"Text\" TEXT NOT NULL, \"Status\" TEXT NOT NULL, \"Reply\" TEXT NOT NULL, \"CreatedAt\" BIGINT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_AiMessages_SessionId_ClientMessageId\" ON \"AiMessages\" (\"SessionId\", \"ClientMessageId\")");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_AiMessages_CreatedAt\" ON \"AiMessages\" (\"CreatedAt\")");
        await ChatBroadcasts.Column(db, "AiSessions", "Channel", "TEXT NOT NULL DEFAULT 'chat'");
        await ChatBroadcasts.Column(db, "AiSessions", "ProtectedOptions", "TEXT NOT NULL DEFAULT ''");
        await ChatBroadcasts.Column(db, "AiSessions", "SecurityVersion", "INTEGER NOT NULL DEFAULT 0");
        await ChatBroadcasts.Column(db, "AiMessages", "SourcesJson", "TEXT NOT NULL DEFAULT '[]'");
        await ChatBroadcasts.Column(db, "AiMessages", "UsageJson", "TEXT NOT NULL DEFAULT '{{}}'");
        await ChatBroadcasts.Column(db, "AiMessages", "DurationMs", "BIGINT NOT NULL DEFAULT 0");
        await ChatBroadcasts.Column(db, "AiMessages", "Sequence", "BIGINT NOT NULL DEFAULT 0");
        if (await db.AiMessages.AnyAsync(m => m.Sequence == 0))
            await db.Database.ExecuteSqlRawAsync("WITH numbered AS (SELECT \"Id\", ROW_NUMBER() OVER (PARTITION BY \"SessionId\" ORDER BY \"CreatedAt\", \"Id\") AS seq FROM \"AiMessages\") UPDATE \"AiMessages\" SET \"Sequence\" = (SELECT seq FROM numbered WHERE numbered.\"Id\" = \"AiMessages\".\"Id\")");
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_AiMessages_SessionId_Sequence\" ON \"AiMessages\" (\"SessionId\", \"Sequence\")");
        await ChatBroadcasts.Column(db, "Computers", "SupportsAiDiagnostics", (db.Database.IsNpgsql() ? "BOOLEAN NOT NULL DEFAULT FALSE" : "INTEGER NOT NULL DEFAULT 0"));
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS \"AiDiagnosticChecks\" (\"Id\" TEXT PRIMARY KEY, \"SessionId\" TEXT NOT NULL REFERENCES \"AiSessions\"(\"Id\") ON DELETE CASCADE, \"OwnerUserId\" INTEGER NOT NULL, \"ClientRequestId\" TEXT NOT NULL, \"MachineName\" TEXT NOT NULL, \"Tool\" TEXT NOT NULL, \"TargetId\" TEXT NOT NULL, \"Status\" TEXT NOT NULL, \"ResultJson\" TEXT NOT NULL, \"CreatedAt\" BIGINT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_AiDiagnosticChecks_SessionId_ClientRequestId\" ON \"AiDiagnosticChecks\" (\"SessionId\", \"ClientRequestId\")");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_AiDiagnosticChecks_OwnerUserId_CreatedAt\" ON \"AiDiagnosticChecks\" (\"OwnerUserId\", \"CreatedAt\")");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS \"AiRequestLogs\" (\"Id\" TEXT PRIMARY KEY, \"OwnerUserId\" INTEGER NOT NULL, \"SessionId\" TEXT NOT NULL, \"Backend\" TEXT NOT NULL, \"ModelId\" TEXT NOT NULL, \"Status\" TEXT NOT NULL, \"CreatedAt\" BIGINT NOT NULL, \"DurationMs\" BIGINT NOT NULL, \"UsageJson\" TEXT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_AiRequestLogs_OwnerUserId_CreatedAt\" ON \"AiRequestLogs\" (\"OwnerUserId\", \"CreatedAt\")");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO \"AiRequestLogs\" (\"Id\",\"OwnerUserId\",\"SessionId\",\"Backend\",\"ModelId\",\"Status\",\"CreatedAt\",\"DurationMs\",\"UsageJson\") SELECT m.\"Id\",s.\"OwnerUserId\",m.\"SessionId\",s.\"Backend\",s.\"ModelId\",m.\"Status\",m.\"CreatedAt\",m.\"DurationMs\",m.\"UsageJson\" FROM \"AiMessages\" m JOIN \"AiSessions\" s ON s.\"Id\"=m.\"SessionId\" WHERE m.\"CreatedAt\" >= {0} ON CONFLICT (\"Id\") DO NOTHING", DateTimeOffset.UtcNow.AddDays(-90).ToUnixTimeSeconds());
        await db.Database.ExecuteSqlRawAsync("INSERT INTO \"AiRequestLogs\" (\"Id\",\"OwnerUserId\",\"SessionId\",\"Backend\",\"ModelId\",\"Status\",\"CreatedAt\",\"DurationMs\",\"UsageJson\") SELECT 'session:' || s.\"Id\",s.\"OwnerUserId\",s.\"Id\",'Session',s.\"Backend\" || ':' || s.\"ModelId\",s.\"Status\",s.\"CreatedAt\",0,'{{}}' FROM \"AiSessions\" s WHERE s.\"CreatedAt\" >= {0} ON CONFLICT (\"Id\") DO NOTHING", DateTimeOffset.UtcNow.AddDays(-90).ToUnixTimeSeconds());
    }
}
