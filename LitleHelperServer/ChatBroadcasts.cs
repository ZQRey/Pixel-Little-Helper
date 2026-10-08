using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;

namespace LitleHelperServer;
public class ChatBroadcast
{
    public string Id { get; set; } = "";
    public int SenderId { get; set; }
    public string Body { get; set; } = "";
    public string Audience { get; set; } = "all";
    public bool Urgent { get; set; }
    public string? Command { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public static class ChatBroadcasts
{
    public static async Task Column(HelperDb db, string table, string name, string type)
    {
        if (!db.Database.IsNpgsql())
        {
            var connection = db.Database.GetDbConnection(); bool open = connection.State == System.Data.ConnectionState.Open; if (!open) await connection.OpenAsync();
            using var command = connection.CreateCommand(); command.CommandText = $"PRAGMA table_info('{table}')"; bool found = false;
            using (var reader = await command.ExecuteReaderAsync()) while (await reader.ReadAsync()) if (reader.GetString(1) == name) found = true;
            if (!open) await connection.CloseAsync(); if (found) return;
        }
        string sql = $"ALTER TABLE \"{table}\" ADD COLUMN {(db.Database.IsNpgsql() ? "IF NOT EXISTS " : "")}\"{name}\" {type}";
        await db.Database.ExecuteSqlRawAsync(sql); // All names and types come only from fixed schema calls.
    }
    public static async Task EnsureSchemaAsync(HelperDb db)
    {
        string date = db.Database.IsNpgsql() ? "timestamp with time zone" : "TEXT", boolean = db.Database.IsNpgsql() ? "boolean" : "INTEGER";
        await Column(db, "ChatMessages", "IsUrgent", boolean + " NOT NULL DEFAULT " + (db.Database.IsNpgsql() ? "FALSE" : "0"));
        await Column(db, "ChatMessages", "BroadcastId", "TEXT NULL"); await Column(db, "ChatMessages", "AcknowledgedAt", date + " NULL");
        await Column(db, "ChatMessages", "Command", "TEXT NULL");
        await Column(db, "ChatGroupMessages", "Command", "TEXT NULL");
        await Column(db, "ChatGroupMessages", "IsUrgent", boolean + " NOT NULL DEFAULT " + (db.Database.IsNpgsql() ? "FALSE" : "0"));
        await Column(db, "ChatFiles", "StorageKey", "TEXT NOT NULL DEFAULT ''");
        string sql = $"CREATE TABLE IF NOT EXISTS \"ChatBroadcasts\" (\"Id\" TEXT PRIMARY KEY, \"SenderId\" INTEGER NOT NULL, \"Body\" TEXT NOT NULL, \"Audience\" TEXT NOT NULL, \"Urgent\" {boolean} NOT NULL, \"CreatedAt\" {date} NOT NULL)"; await db.Database.ExecuteSqlRawAsync(sql);
        await Column(db, "ChatBroadcasts", "Command", "TEXT NULL");
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/effects", async (HelperDb db, ClaimsPrincipal p) => await ChatCommands.PendingAsync(db, (await Messenger.UserAsync(db,p)).Id));
        api.MapGet("/capabilities", async (HelperDb db, ClaimsPrincipal p) => { await Messenger.UserAsync(db, p); return new { canBroadcast = true }; });
        api.MapGet("/urgent", async (HelperDb db, ClaimsPrincipal p) =>
        {
            var me = await Messenger.UserAsync(db, p); var since = DateTime.UtcNow.AddHours(-1);
            return await db.ChatMessages.Where(m => m.RecipientId == me.Id && m.IsUrgent && m.AcknowledgedAt == null && m.SentAt > since).OrderBy(m => m.Id).Take(100).ToListAsync();
        });
        api.MapPost("/ack/{id:long}", async (long id, HelperDb db, ClaimsPrincipal p, IHubContext<MessengerHub> hub) =>
        {
            var me = await Messenger.UserAsync(db, p); var message = await db.ChatMessages.SingleOrDefaultAsync(m => m.Id == id && m.RecipientId == me.Id && m.IsUrgent) ?? throw new UnauthorizedAccessException();
            if (message.AcknowledgedAt == null) { message.AcknowledgedAt = DateTime.UtcNow; await db.SaveChangesAsync(); }
            await hub.Clients.Groups("Chat:" + me.Id, "Chat:" + message.SenderId).SendAsync("ChatChanged"); return Results.Ok();
        });
        api.MapGet("/broadcasts", async (HelperDb db, ClaimsPrincipal p) =>
        {
            var me = await Messenger.UserAsync(db, p);
            return await db.ChatBroadcasts.Where(b => b.SenderId == me.Id).OrderByDescending(b => b.CreatedAt).Take(50).Select(b => new { b.Id, b.Body, b.Urgent, b.Audience, b.CreatedAt, recipients = db.ChatMessages.Count(m => m.BroadcastId == b.Id), read = db.ChatMessages.Count(m => m.BroadcastId == b.Id && m.ReadAt != null), acknowledged = db.ChatMessages.Count(m => m.BroadcastId == b.Id && m.AcknowledgedAt != null) }).ToListAsync();
        });
        api.MapPost("/broadcast", async (HttpRequest request, HelperDb db, ClaimsPrincipal p, IConfiguration config, ChatPresence presence, ChatGroupGate gate, IHubContext<MessengerHub> hub, CancellationToken token) =>
        {
            var me = await Messenger.UserAsync(db, p);
            var feature = request.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>(); if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = 128 * 1024 * 1024;
            if (!request.HasFormContentType) throw new ArgumentException("Неверный формат рассылки.");
            var form = await request.ReadFormAsync(token); string audience = form["audience"].ToString(), body = form["body"].ToString().Trim(); bool urgent = form["urgent"] == "true";
            if (!Guid.TryParse(form["clientId"], out var key) || audience is not ("all" or "online" or "selected") || body.Length > 4000 || body.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t')) throw new ArgumentException("Проверьте параметры рассылки.");
            var selected = new HashSet<int>();
            foreach (var value in form["recipients"]) { if (!int.TryParse(value, out int user) || user <= 0) throw new ArgumentException("Неверный получатель."); selected.Add(user); }
            if (selected.Count > 5000 || audience == "selected" && selected.Count == 0) throw new ArgumentException("Выберите получателей.");
            var uploads = form.Files.ToArray(); if (uploads.Length > 10 || uploads.Any(f => f.Length > ChatFiles.MaxFile) || uploads.Sum(f => f.Length) > ChatFiles.MaxTotal) throw new ArgumentException("Вложения: до 10 файлов, до 50 МБ каждый и 100 МБ всего.");
            if (body.Length == 0 && uploads.Length > 0) body = "Вложения: " + string.Join(", ", uploads.Select(f => ChatFiles.Name(f.FileName))); if (body.Length == 0) throw new ArgumentException("Введите сообщение.");
            var content = ChatCommands.Parse(body); body = content.Body; urgent |= content.Urgent;
            var saved = new List<ChatFile>(); string directory = ChatFiles.Folder(config); Directory.CreateDirectory(directory); bool committed = false;
            await gate.Semaphore.WaitAsync(token);
            try
            {
                foreach (var upload in uploads)
                {
                    var file = new ChatFile { Id = Guid.NewGuid().ToString("N"), Name = ChatFiles.Name(upload.FileName), Size = upload.Length, Order = saved.Count }; saved.Add(file);
                    await using (var output = File.Create(Path.Combine(directory, file.Id))) await upload.CopyToAsync(output, token);
                    await using var source = File.OpenRead(Path.Combine(directory, file.Id)); file.Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(source, token));
                }
                string id = key.ToString("N"); var old = await db.ChatBroadcasts.SingleOrDefaultAsync(b => b.Id == id, token);
                if (old != null)
                {
                    if (old.SenderId != me.Id || old.Body != body || old.Audience != audience || old.Urgent != urgent || old.Command != content.Command) throw new ArgumentException("Идентификатор рассылки уже использован.");
                    if (audience == "selected" && !selected.SetEquals(await db.ChatMessages.Where(m => m.BroadcastId == id).Select(m => m.RecipientId).ToListAsync(token))) throw new ArgumentException("Получатели рассылки изменились.");
                    var first = await db.ChatMessages.Where(m => m.BroadcastId == id).OrderBy(m => m.Id).FirstAsync(token);
                    var files = await db.ChatFiles.Where(f => f.MessageId == first.Id && f.PeerId == first.RecipientId && f.SenderId == me.Id).OrderBy(f => f.Order).ToListAsync(token);
                    if (!files.Select(f => (f.Name, f.Size, f.Sha256)).SequenceEqual(saved.Select(f => (f.Name, f.Size, f.Sha256)))) throw new ArgumentException("Вложения рассылки изменились.");
                    return Results.Ok(new { id, recipients = await db.ChatMessages.CountAsync(m => m.BroadcastId == id, token) });
                }
                var recipients = await db.Users.Where(u => u.Id != me.Id && u.IsActive && u.PasswordHash == "!AD").Select(u => u.Id).ToListAsync(token);
                if (audience == "selected") { if (selected.Contains(me.Id) || selected.Any(id => !recipients.Contains(id))) throw new ArgumentException("Получатель отключён или недоступен."); recipients = recipients.Where(selected.Contains).ToList(); }
                if (audience == "online") recipients = recipients.Where(presence.Online).ToList(); if (recipients.Count == 0) throw new ArgumentException("Нет получателей для выбранного режима.");
                var minute = DateTime.UtcNow.AddMinutes(-1);
                if (await db.ChatBroadcasts.AnyAsync(b => b.SenderId == me.Id && b.CreatedAt > minute, token)) return Results.Json(new { error = "Массовая рассылка доступна раз в минуту. Повторите позже." }, statusCode: 429);
                await using var transaction = await db.Database.BeginTransactionAsync(token);
                db.ChatBroadcasts.Add(new() { Id = id, SenderId = me.Id, Body = body, Audience = audience, Urgent = urgent, Command = content.Command });
                var messages = recipients.Select(user => new ChatMessage { SenderId = me.Id, RecipientId = user, Body = body, IsUrgent = urgent, Command = content.Command, BroadcastId = id, ClientId = Guid.NewGuid().ToString("N") }).ToArray(); db.ChatMessages.AddRange(messages); await db.SaveChangesAsync(token);
                foreach (var message in messages) foreach (var file in saved) db.ChatFiles.Add(new() { Id = Guid.NewGuid().ToString("N"), StorageKey = file.Id, SenderId = me.Id, PeerId = message.RecipientId, MessageId = message.Id, Name = file.Name, Size = file.Size, Sha256 = file.Sha256, Order = file.Order });
                await db.SaveChangesAsync(token); await transaction.CommitAsync(token); committed = true; await ChatFiles.PopulateAsync(db, messages);
                foreach (var message in messages) await hub.Clients.Groups("Chat:" + message.RecipientId, "Chat:" + me.Id).SendAsync("ChatMessage", message, token);
                return Results.Ok(new { id, recipients = recipients.Count });
            }
            finally { if (!committed) foreach (var file in saved) File.Delete(Path.Combine(directory, file.Id)); gate.Semaphore.Release(); }
        });
    }
}
