using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;

namespace LitleHelperServer;
public class ChatFile
{
    public string Id { get; set; } = "";
    public string StorageKey { get; set; } = "";
    [System.ComponentModel.DataAnnotations.Schema.NotMapped] public string PathKey => StorageKey.Length == 0 ? Id : StorageKey;
    public int SenderId { get; set; }
    public int PeerId { get; set; }
    public long MessageId { get; set; }
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public int Order { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public record ChatFileInfo(string Id, string Name, long Size);
public static class ChatFiles
{
    public const long MaxFile = 50 * 1024 * 1024, MaxTotal = 100 * 1024 * 1024;
    public static string Folder(IConfiguration config) => config["Messenger:FilesDirectory"] ?? Path.Combine(Path.GetDirectoryName(config["Messenger:SettingsFile"] ?? "data/messenger.json")!, "chat-files");
    public static async Task EnsureSchemaAsync(HelperDb db)
    {
        string date = db.Database.IsNpgsql() ? "timestamp with time zone" : "TEXT";
        string sql = $"CREATE TABLE IF NOT EXISTS \"ChatFiles\" (\"Id\" TEXT PRIMARY KEY, \"SenderId\" INTEGER NOT NULL, \"PeerId\" INTEGER NOT NULL, \"MessageId\" BIGINT NOT NULL, \"Name\" TEXT NOT NULL, \"Size\" BIGINT NOT NULL, \"Sha256\" TEXT NOT NULL, \"Order\" INTEGER NOT NULL, \"CreatedAt\" {date} NOT NULL)";
        await db.Database.ExecuteSqlRawAsync(sql); // Provider-specific type is a fixed literal.
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_ChatFiles_PeerId_MessageId\" ON \"ChatFiles\" (\"PeerId\", \"MessageId\")");
    }
    public static async Task PopulateAsync(HelperDb db, IEnumerable<ChatMessage> messages)
    {
        var rows = messages.ToArray(); if (rows.Length == 0) return;
        var ids = rows.Select(m => m.Id).Distinct().ToArray();
        var files = await db.ChatFiles.AsNoTracking().Where(f => ids.Contains(f.MessageId)).OrderBy(f => f.Order).ToListAsync();
        foreach (var message in rows) message.Attachments = files.Where(f => f.MessageId == message.Id && f.SenderId == message.SenderId && f.PeerId == message.RecipientId).Select(f => new ChatFileInfo(f.Id, f.Name, f.Size)).ToList();
    }
    public static string Name(string name)
    {
        var safe = Path.GetFileName(name.Replace('\\', '/')); safe = new string(safe.Where(c => !char.IsControl(c)).Take(200).ToArray());
        return string.IsNullOrWhiteSpace(safe) || safe is "." or ".." ? "file" : safe;
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/files/{id}", async (string id, HelperDb db, ClaimsPrincipal principal, IConfiguration config, HttpResponse response) =>
        {
            var me = await Messenger.UserAsync(db, principal);
            if (!Guid.TryParseExact(id, "N", out _)) return Results.NotFound();
            var file = await db.ChatFiles.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id); if (file == null) return Results.NotFound();
            bool allowed;
            if (file.PeerId < 0)
                allowed = await db.ChatGroupMembers.AnyAsync(m => m.GroupId == -file.PeerId && m.UserId == me.Id && m.JoinedAfterId < file.MessageId) && await db.ChatGroupMessages.AnyAsync(m => m.Id == file.MessageId && m.GroupId == -file.PeerId && m.SenderId == file.SenderId && !db.ChatGroupRestrictions.Any(r => r.GroupId == m.GroupId && r.UserId == me.Id && (r.EndsAt > DateTime.UtcNow || m.SentAt >= r.StartedAt && m.SentAt < r.EndsAt)));
            else allowed = (me.Id == file.SenderId || me.Id == file.PeerId) && await db.ChatMessages.AnyAsync(m => m.Id == file.MessageId && m.SenderId == file.SenderId && m.RecipientId == file.PeerId);
            if (!allowed) return Results.NotFound();
            string path = Path.Combine(Folder(config), file.PathKey); if (!File.Exists(path)) return Results.NotFound();
            response.Headers.CacheControl = "no-store"; response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.File(path, "application/octet-stream", file.Name, enableRangeProcessing: true);
        });
        api.MapPost("/files/send", async (HttpRequest request, HelperDb db, ClaimsPrincipal principal, IConfiguration config, IHubContext<MessengerHub> hub, ChatGroupGate gate, CancellationToken cancellation) =>
        {
            var me = await Messenger.UserAsync(db, principal);
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "Используйте загрузку файлов multipart/form-data." });
            var size = request.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (size is { IsReadOnly: false }) size.MaxRequestBodySize = 128 * 1024 * 1024;
            IFormCollection form;
            try { form = await request.ReadFormAsync(cancellation); }
            catch (InvalidDataException) { return Results.BadRequest(new { error = "Неверные данные или превышен размер вложений." }); }
            catch (BadHttpRequestException) { return Results.Json(new { error = "Превышен размер сообщения с вложениями." }, statusCode: 413); }
            if (!int.TryParse(form["peer"], out int peer) || peer == 0 || peer == me.Id || peer == int.MinValue || !Guid.TryParse(form["clientId"], out var key)) throw new ArgumentException("Выберите чат.");
            string body = form["body"].ToString().Trim();
            if (body.Length > 4000 || body.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t')) throw new ArgumentException("Некорректный текст сообщения.");
            var uploads = form.Files.ToArray();
            if (uploads.Length is < 1 or > 10 || uploads.Any(f => f.Length > MaxFile) || uploads.Sum(f => f.Length) > MaxTotal) throw new ArgumentException("Не более 10 файлов: до 50 МБ каждый и до 100 МБ на сообщение.");
            if (body.Length == 0) body = "Вложения: " + string.Join(", ", uploads.Select(f => Name(f.FileName)));
            string directory = Folder(config); Directory.CreateDirectory(directory); var saved = new List<ChatFile>(); bool committed = false;
            await gate.Semaphore.WaitAsync(cancellation);
            try
            {
                if (peer < 0)
                {
                    await ChatGroups.Member(db, -peer, me.Id);
                    if (!await db.ChatGroups.AnyAsync(g => g.Id == -peer && !g.IsClosed, cancellation)) throw new ArgumentException("Группа закрыта.");
                }
                else if (!await db.Users.AnyAsync(u => u.Id == peer && u.IsActive && u.PasswordHash == "!AD", cancellation)) throw new ArgumentException("Получатель недоступен.");
                foreach (var upload in uploads)
                {
                    var file = new ChatFile { Id = Guid.NewGuid().ToString("N"), SenderId = me.Id, PeerId = peer, Name = Name(upload.FileName), Size = upload.Length, Order = saved.Count }; saved.Add(file);
                    await using (var output = new FileStream(Path.Combine(directory, file.Id), FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true)) await upload.CopyToAsync(output, cancellation);
                    await using var data = File.OpenRead(Path.Combine(directory, file.Id)); file.Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(data, cancellation));
                }
                string clientId = key.ToString("N"); ChatMessage? old;
                if (peer < 0) { var group = await db.ChatGroupMessages.SingleOrDefaultAsync(m => m.SenderId == me.Id && m.ClientId == clientId, cancellation); old = group == null ? null : new() { Id = group.Id, SenderId = group.SenderId, RecipientId = -group.GroupId, Body = group.Body, ClientId = group.ClientId, SentAt = group.SentAt, SenderName = me.FullName }; }
                else old = await db.ChatMessages.SingleOrDefaultAsync(m => m.SenderId == me.Id && m.ClientId == clientId, cancellation);
                if (old != null)
                {
                    var previous = await db.ChatFiles.Where(f => f.SenderId == me.Id && f.PeerId == peer && f.MessageId == old.Id).OrderBy(f => f.Order).ToListAsync(cancellation);
                    if (old.RecipientId != peer || old.Body != body || !previous.Select(f => (f.Name, f.Size, f.Sha256)).SequenceEqual(saved.Select(f => (f.Name, f.Size, f.Sha256)))) throw new ArgumentException("Идентификатор сообщения уже использован.");
                    await PopulateAsync(db, [old]); return Results.Ok(old);
                }
                await using var transaction = await db.Database.BeginTransactionAsync(cancellation);
                ChatMessage result;
                if (peer < 0)
                {
                    var message = new ChatGroupMessage { SenderId = me.Id, GroupId = -peer, Body = body, ClientId = clientId }; db.ChatGroupMessages.Add(message); await db.SaveChangesAsync(cancellation);
                    result = new() { Id = message.Id, SenderId = me.Id, RecipientId = peer, Body = body, ClientId = clientId, SentAt = message.SentAt, SenderName = me.FullName };
                }
                else { result = new() { SenderId = me.Id, RecipientId = peer, Body = body, ClientId = clientId }; db.ChatMessages.Add(result); await db.SaveChangesAsync(cancellation); }
                foreach (var file in saved) file.MessageId = result.Id;
                db.ChatFiles.AddRange(saved); await db.SaveChangesAsync(cancellation); await transaction.CommitAsync(cancellation); committed = true;
                result.Attachments = saved.Select(f => new ChatFileInfo(f.Id, f.Name, f.Size)).ToList();
                var targets = peer < 0 ? await db.ChatGroupMembers.Where(m => m.GroupId == -peer && db.Users.Any(u => u.Id == m.UserId && u.IsActive) && !db.ChatGroupRestrictions.Any(r => r.GroupId == -peer && r.UserId == m.UserId && r.EndsAt > DateTime.UtcNow)).Select(m => "Chat:" + m.UserId).ToArrayAsync(cancellation) : ["Chat:" + me.Id, "Chat:" + peer];
                await hub.Clients.Groups(targets).SendAsync("ChatMessage", result, cancellation); return Results.Ok(result);
            }
            finally { if (!committed) foreach (var file in saved) File.Delete(Path.Combine(directory, file.Id)); gate.Semaphore.Release(); }
        }).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(128 * 1024 * 1024));
    }
    public static async Task CleanupAsync(HelperDb db, IConfiguration config, CancellationToken token)
    {
        var files = await db.ChatFiles.Where(f => f.PeerId < 0 ? !db.ChatGroupMessages.Any(m => m.Id == f.MessageId && m.GroupId == -f.PeerId && m.SenderId == f.SenderId) : !db.ChatMessages.Any(m => m.Id == f.MessageId && m.SenderId == f.SenderId && m.RecipientId == f.PeerId)).ToListAsync(token);
        db.ChatFiles.RemoveRange(files); await db.SaveChangesAsync(token);
        var known = (await db.ChatFiles.Select(f => f.StorageKey == "" ? f.Id : f.StorageKey).ToListAsync(token)).ToHashSet();
        foreach (var file in files) if (!known.Contains(file.PathKey)) File.Delete(Path.Combine(Folder(config), file.PathKey));
        string directory = Folder(config); if (!Directory.Exists(directory)) return;
        foreach (string path in Directory.EnumerateFiles(directory)) if (Guid.TryParseExact(Path.GetFileName(path), "N", out _) && !known.Contains(Path.GetFileName(path)) && File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-1)) File.Delete(path);
    }
}
