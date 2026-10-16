using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LitleHelperServer;

public class TicketAttachment
{
    public string Id { get; set; } = "";
    public int TicketId { get; set; }
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";
    public string StorageKey { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public record TicketAttachmentDto(string FileName, byte[] Content);
public record TicketAttachmentInfo(string Id, int TicketId, string FileName, long FileSize, DateTime CreatedAt);

public static class TicketAttachments
{
    public const long MaxFileSize = 20 * 1024 * 1024; // 20 MB
    public const int MaxAttachmentsPerTicket = 5;

    public static string Folder(IConfiguration config)
    {
        string configured = config["Tickets:AttachmentsDirectory"] ?? "";
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        string baseDir = Path.GetDirectoryName(config["Messenger:SettingsFile"] ?? "data/messenger.json") ?? "data";
        return Path.GetFullPath(Path.Combine(baseDir, "ticket-attachments"));
    }

    public static async Task EnsureSchemaAsync(HelperDb db)
    {
        string date = db.Database.IsNpgsql() ? "timestamp with time zone" : "TEXT";
        string sql = $"CREATE TABLE IF NOT EXISTS \"TicketAttachments\" (\"Id\" TEXT PRIMARY KEY, \"TicketId\" INTEGER NOT NULL, \"FileName\" TEXT NOT NULL, \"FileSize\" BIGINT NOT NULL, \"ContentType\" TEXT NOT NULL, \"StorageKey\" TEXT NOT NULL, \"CreatedAt\" {date} NOT NULL)";
        await db.Database.ExecuteSqlRawAsync(sql);
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_TicketAttachments_TicketId\" ON \"TicketAttachments\" (\"TicketId\")");
    }

    public static string SanitizeFileName(string name)
    {
        string safe = Path.GetFileName(name.Replace('\\', '/'));
        safe = new string(safe.Where(c => !char.IsControl(c)).Take(180).ToArray());
        return string.IsNullOrWhiteSpace(safe) || safe is "." or ".." ? "attachment" : safe;
    }

    public static string DetectContentType(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            ".txt" or ".log" => "text/plain",
            ".json" => "application/json",
            ".csv" => "text/csv",
            ".zip" => "application/zip",
            ".rar" => "application/x-rar-compressed",
            ".7z" => "application/x-7z-compressed",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };
    }

    public static async Task<TicketAttachment> SaveAsync(HelperDb db, IConfiguration config, int ticketId, string fileName, byte[] data, CancellationToken token = default)
    {
        if (data.Length > MaxFileSize)
            throw new ArgumentException($"Размер файла {fileName} превышает допустимый предел 20 МБ.");

        string id = Guid.NewGuid().ToString("N");
        string safeName = SanitizeFileName(fileName);
        string folder = Folder(config);
        Directory.CreateDirectory(folder);

        string ext = Path.GetExtension(safeName);
        string storageKey = $"{ticketId}_{id}{ext}";
        string fullPath = Path.Combine(folder, storageKey);

        await File.WriteAllBytesAsync(fullPath, data, token);

        var attachment = new TicketAttachment
        {
            Id = id,
            TicketId = ticketId,
            FileName = safeName,
            FileSize = data.Length,
            ContentType = DetectContentType(safeName),
            StorageKey = storageKey,
            CreatedAt = DateTime.UtcNow
        };

        db.TicketAttachments.Add(attachment);
        await db.SaveChangesAsync(token);
        return attachment;
    }

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/tickets/{ticketId:int}/attachments", async (int ticketId, HelperDb db, ClaimsPrincipal p) =>
        {
            var ticket = await db.Tickets.FindAsync(ticketId);
            if (ticket == null) return Results.NotFound();

            if (!Access.Can(p, "tickets.all") && !ticket.Username.Equals(Security.TicketOwner(p), StringComparison.OrdinalIgnoreCase))
                return Results.Forbid();

            var list = await db.TicketAttachments.AsNoTracking()
                .Where(a => a.TicketId == ticketId)
                .OrderBy(a => a.CreatedAt)
                .Select(a => new TicketAttachmentInfo(a.Id, a.TicketId, a.FileName, a.FileSize, a.CreatedAt))
                .ToListAsync();

            return Results.Ok(list);
        });

        api.MapGet("/tickets/{ticketId:int}/attachments/{id}", async (int ticketId, string id, HelperDb db, ClaimsPrincipal p, IConfiguration config, HttpResponse response) =>
        {
            var ticket = await db.Tickets.FindAsync(ticketId);
            if (ticket == null) return Results.NotFound();

            if (!Access.Can(p, "tickets.all") && !ticket.Username.Equals(Security.TicketOwner(p), StringComparison.OrdinalIgnoreCase))
                return Results.Forbid();

            var file = await db.TicketAttachments.AsNoTracking().SingleOrDefaultAsync(a => a.TicketId == ticketId && a.Id == id);
            if (file == null) return Results.NotFound();

            string fullPath = Path.Combine(Folder(config), file.StorageKey);
            if (!File.Exists(fullPath)) return Results.NotFound();

            response.Headers.CacheControl = "no-store";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.File(fullPath, file.ContentType, file.FileName, enableRangeProcessing: true);
        });
    }
}
