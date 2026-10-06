using Microsoft.EntityFrameworkCore;
using Novell.Directory.Ldap;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LitleHelperServer;

public class TelegramBotState { [Key] public string Id { get; set; } = ""; public long Offset { get; set; } }
public class TelegramReplySession
{
    [Key] public long Id { get; set; }
    public int TicketId { get; set; }
    public string Mode { get; set; } = "reply";
    public string BotKey { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}
public class TelegramHandledUpdate
{
    [Key] public string Id { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public record TelegramActor(PanelUser User, int GlpiUserId);
public interface ITelegramDirectory { Task<AdIdentity> FindAsync(long telegramId, CancellationToken token); }
public class TelegramDirectory(IntegrationSettings settings) : ITelegramDirectory
{
    public async Task<AdIdentity> FindAsync(long telegramId, CancellationToken token)
    {
        var ad = settings.Ad(); var telegram = settings.Telegram();
        if (!ad.Enabled || telegramId <= 0 || telegram.DirectoryLogin.Length == 0 || telegram.DirectoryPassword.Length == 0)
            throw new UnauthorizedAccessException("Настройте чтение AD в разделе Telegram.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var ldap = AdAuthentication.Connection(ad);
        try
        {
            await ldap.ConnectAsync(ad.Host, ad.Port, timeout.Token);
            string login = telegram.DirectoryLogin.Contains('@') || telegram.DirectoryLogin.Contains('=') || telegram.DirectoryLogin.Contains('\\') ? telegram.DirectoryLogin : telegram.DirectoryLogin + "@" + ad.Domain;
            await ldap.BindAsync(login, telegram.DirectoryPassword, timeout.Token);
            var constraints = ldap.SearchConstraints; constraints.ReferralFollowing = false; ldap.Constraints = constraints;
            var results = await ldap.SearchAsync(ad.BaseDn, LdapConnection.ScopeSub,
                $"(&(objectCategory=person)(objectClass=user)({telegram.IdAttribute}={telegramId}))",
                ["sAMAccountName", "displayName", "userAccountControl"], false, timeout.Token);
            LdapEntry? entry = null;
            while (await results.HasMoreAsync(timeout.Token))
            {
                LdapEntry next; try { next = await results.NextAsync(timeout.Token); } catch (LdapReferralException) { continue; }
                if (entry != null) throw new UnauthorizedAccessException("Telegram ID встречается в AD несколько раз. Исправьте дубликат.");
                entry = next;
            }
            if (entry == null || (int.Parse(entry.Get("userAccountControl").StringValue) & 2) != 0)
                throw new UnauthorizedAccessException("Telegram ID не найден в активной учётной записи AD.");
            string account = AdAuthentication.Account(entry.Get("sAMAccountName").StringValue, ad);
            return new(account + "@" + ad.Domain, entry.GetStringValueOrDefault("displayName", account) ?? account, account);
        }
        catch (LdapException) { throw new InvalidOperationException("Не удалось прочитать AD. Проверьте учётную запись чтения, LDAPS и Base DN."); }
    }
}

public class TicketManagementGate { public SemaphoreSlim Semaphore { get; } = new(1, 1); }
public class TicketManagement(HelperDb db, GlpiService glpi, TicketManagementGate gate)
{
    public static int Status(string value) => value == "Created" ? 1 : int.TryParse(value, out int n) && n is >= 1 and <= 6 ? n : 1;
    public static string StatusName(int n) => n switch { 1 => "Новая", 2 => "В работе (назначена)", 3 => "В работе (запланирована)", 4 => "Ожидание", 5 => "Выполнена (решена)", 6 => "Закрыта", _ => "Неизвестно" };
    private async Task SyncUnlockedAsync(TicketRecord ticket, CancellationToken token)
    {
        var data = await glpi.GetTicketAsync(ticket.GlpiId, token);
        if (data.TryGetProperty("is_deleted", out var deleted) && GlpiService.Number(deleted) != 0)
            throw new InvalidOperationException("Заявка удалена в GLPI.");
        ticket.Status = GlpiService.Number(data.GetProperty("status")).ToString();
        int[] assignees = await glpi.AssigneesAsync(ticket.GlpiId, token);
        if (!assignees.Contains(ticket.AssignedGlpiUserId)) { ticket.AssignedGlpiUserId = assignees.FirstOrDefault(); ticket.AssignedUsername = ""; }
        ticket.SyncedAt = DateTime.UtcNow; await db.SaveChangesAsync(token);
    }
    public async Task SyncAsync(TicketRecord ticket, CancellationToken token)
    {
        await gate.Semaphore.WaitAsync(token); try { await db.Entry(ticket).ReloadAsync(token); await SyncUnlockedAsync(ticket, token); } finally { gate.Semaphore.Release(); }
    }
    public async Task ClaimAsync(TicketRecord ticket, TelegramActor actor, CancellationToken token)
    {
        await gate.Semaphore.WaitAsync(token);
        try
        {
            await db.Entry(ticket).ReloadAsync(token); await SyncUnlockedAsync(ticket, token);
            if (Status(ticket.Status) >= 5) throw new InvalidOperationException("Заявка уже выполнена или закрыта.");
            int[] assigned = await glpi.AssigneesAsync(ticket.GlpiId, token);
            if (assigned.Any(id => id != actor.GlpiUserId)) throw new InvalidOperationException("Заявка уже назначена другому исполнителю в GLPI.");
            if (assigned.Length == 0) await glpi.AssignAsync(ticket.GlpiId, actor.GlpiUserId, token);
            assigned = await glpi.AssigneesAsync(ticket.GlpiId, token);
            if (assigned.Length != 1 || assigned[0] != actor.GlpiUserId) throw new InvalidOperationException("GLPI не подтвердил назначение. Проверьте права API и исполнителя.");
            ticket.AssignedGlpiUserId = actor.GlpiUserId; ticket.AssignedUsername = actor.User.Username;
            await SyncUnlockedAsync(ticket, token);
            db.AuditLogs.Add(new() { AdminUsername = actor.User.Username, MachineName = ticket.MachineName, CommandType = "telegram_ticket_claim", CommandPayload = "GLPI #" + ticket.GlpiId, Status = "Completed", Result = "Назначен GLPI User #" + actor.GlpiUserId });
            await db.SaveChangesAsync(token);
        }
        finally { gate.Semaphore.Release(); }
    }
    public async Task ActAsync(TicketRecord ticket, TelegramActor actor, string action, string text, int status, CancellationToken token)
    {
        await gate.Semaphore.WaitAsync(token);
        try
        {
            await db.Entry(ticket).ReloadAsync(token); await SyncUnlockedAsync(ticket, token);
            if (!(await glpi.AssigneesAsync(ticket.GlpiId, token)).Contains(actor.GlpiUserId))
                throw new UnauthorizedAccessException("Вы не являетесь исполнителем этой заявки в GLPI.");
            if (Status(ticket.Status) == 6) throw new InvalidOperationException("Заявка закрыта.");
            if (action is "reply" or "solve")
            {
                if (string.IsNullOrWhiteSpace(text) || text.Length > 8000) throw new ArgumentException("Текст должен содержать от 1 до 8000 символов.");
                if (action == "reply") await glpi.FollowupAsync(ticket.GlpiId, actor.User.Username, text, token);
                else await glpi.SolveAsync(ticket.GlpiId, actor.User.Username, text, token);
            }
            else
            {
                if (status == 6 && Status(ticket.Status) != 5) throw new InvalidOperationException("Сначала добавьте решение заявки.");
                if (status is not (2 or 3 or 4 or 6)) throw new ArgumentException("Неверный статус.");
                await glpi.UpdateTicketAsync(ticket.GlpiId, status, token);
            }
            await SyncUnlockedAsync(ticket, token);
            db.AuditLogs.Add(new() { AdminUsername = actor.User.Username, MachineName = ticket.MachineName, CommandType = "telegram_ticket_" + action, CommandPayload = "GLPI #" + ticket.GlpiId, Status = "Completed", Result = "Статус " + ticket.Status });
            if (action == "solve" && Status(ticket.Status) != 5) throw new InvalidOperationException("GLPI сохранил решение, но не подтвердил статус «Выполнена». Проверьте заявку в GLPI.");
            if (action == "status" && Status(ticket.Status) != status) throw new InvalidOperationException("GLPI не подтвердил изменение статуса.");
            await db.SaveChangesAsync(token);
        }
        finally { gate.Semaphore.Release(); }
    }
    public async Task SetStatusAsync(TicketRecord ticket, int status, CancellationToken token)
    {
        await gate.Semaphore.WaitAsync(token);
        try { await glpi.UpdateTicketAsync(ticket.GlpiId, status, token); await SyncUnlockedAsync(ticket, token); if (Status(ticket.Status) != status) throw new InvalidOperationException("GLPI не подтвердил изменение статуса."); }
        finally { gate.Semaphore.Release(); }
    }
    public static async Task EnsureSchemaAsync(HelperDb db)
    {
        string timestamp = db.Database.IsNpgsql() ? "timestamp with time zone" : "TEXT";
        foreach (var column in new[] { ("AssignedGlpiUserId", "INTEGER NOT NULL DEFAULT 0"), ("AssignedUsername", "TEXT NOT NULL DEFAULT ''"), ("SyncedAt", timestamp + " NULL") })
        {
            if (db.Database.IsNpgsql()) { string sql = "ALTER TABLE \"Tickets\" ADD COLUMN IF NOT EXISTS \"" + column.Item1 + "\" " + column.Item2; await db.Database.ExecuteSqlRawAsync(sql); }
            else
            {
                await db.Database.OpenConnectionAsync(); using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = "PRAGMA table_info('Tickets')";
                bool exists = false; using (var reader = await command.ExecuteReaderAsync()) while (await reader.ReadAsync()) if (reader.GetString(1) == column.Item1) exists = true;
                if (!exists) { string sql = "ALTER TABLE \"Tickets\" ADD COLUMN \"" + column.Item1 + "\" " + column.Item2; await db.Database.ExecuteSqlRawAsync(sql); }
                await db.Database.CloseConnectionAsync();
            }
        }
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS \"TelegramBotStates\" (\"Id\" TEXT PRIMARY KEY, \"Offset\" BIGINT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS \"TelegramReplySessions\" (\"Id\" BIGINT PRIMARY KEY, \"TicketId\" INTEGER NOT NULL, \"Mode\" TEXT NOT NULL, \"BotKey\" TEXT NOT NULL, \"ExpiresAt\" " + timestamp + " NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS \"TelegramHandledUpdates\" (\"Id\" TEXT PRIMARY KEY, \"CreatedAt\" " + timestamp + " NOT NULL)");
    }
}

public class TelegramBotHandler(HelperDb db, ITelegramDirectory directory, GlpiService glpi, TicketManagement tickets, TelegramClient telegram, IntegrationSettings settings)
{
    public async Task<TelegramActor> ActorAsync(long id, CancellationToken token)
    {
        var identity = await directory.FindAsync(id, token);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Username == identity.Username && u.PasswordHash == "!AD", token);
        if (user == null || !user.IsActive || !Access.Can(user, "tickets.manage") || !Access.Can(user, "tickets.all"))
            throw new UnauthorizedAccessException("Войдите в Control Center через AD. Администратор должен выдать права просмотра всех заявок и управления заявками.");
        return new(user, await glpi.FindTechnicianAsync(identity.AccountName, token));
    }
    private static object Button(string text, string data) => new { text, callback_data = data };
    private async Task CardAsync(long chat, TicketRecord ticket, TelegramActor actor, CancellationToken token)
    {
        await tickets.SyncAsync(ticket, token);
        if (!(await glpi.AssigneesAsync(ticket.GlpiId, token)).Contains(actor.GlpiUserId)) throw new UnauthorizedAccessException("Эта заявка назначена другому исполнителю.");
        var history = await glpi.FollowupsAsync(ticket.GlpiId, token);
        string comments = string.Join("\n", history.EnumerateArray().Where(r => !r.TryGetProperty("is_private", out var p) || GlpiService.Number(p) == 0).TakeLast(5).Select(r => r.TryGetProperty("content", out var c) ? Plain(c.GetString() ?? "") : ""));
        string text = $"Заявка GLPI #{ticket.GlpiId}\n{TicketManagement.StatusName(TicketManagement.Status(ticket.Status))}\n{ticket.Username} · {ticket.MachineName}\n{ticket.Title}\n{ticket.Description}\n\nПоследние ответы:\n{comments}";
        object[][] buttons = TicketManagement.Status(ticket.Status) == 6 ? [] :
            [[Button("Ответить", "reply:" + ticket.Id), Button("Добавить решение", "solve:" + ticket.Id)],
             [Button("В работе", "status:" + ticket.Id + ":2"), Button("Ожидание", "status:" + ticket.Id + ":4")],
             [Button("Закрыть", "status:" + ticket.Id + ":6"), Button("Обновить", "open:" + ticket.Id)]];
        await telegram.SendToAsync(chat.ToString(), Limit(text), new { inline_keyboard = buttons }, token);
    }
    public static string Plain(string text) => System.Net.WebUtility.HtmlDecode(Regex.Replace(text.Replace("<br>", "\n").Replace("<br />", "\n").Replace("</p>", "\n"), "<[^>]*>", ""));
    private static string Limit(string value) => value.Length <= 3800 ? value : value[..(char.IsHighSurrogate(value[3799]) ? 3799 : 3800)] + "…";
    public async Task HandleAsync(JsonElement update, CancellationToken token)
    {
        bool callback = update.TryGetProperty("callback_query", out var item);
        if (!callback && !update.TryGetProperty("message", out item)) return;
        if (!item.TryGetProperty("from", out var from) || from.GetProperty("is_bot").GetBoolean()) return;
        long sender = from.GetProperty("id").GetInt64();
        string? callbackId = callback ? item.GetProperty("id").GetString() : null;
        long chat = 0; string chatType = "";
        JsonElement message = callback && item.TryGetProperty("message", out var m) ? m : item;
        if (message.TryGetProperty("chat", out var c)) { chat = c.GetProperty("id").GetInt64(); chatType = c.GetProperty("type").GetString() ?? ""; }
        string command = callback ? item.GetProperty("data").GetString() ?? "" : item.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        if (callback && command == "claimed") { await telegram.AnswerCallbackAsync(callbackId!, "Заявка уже принята", token); return; }
        if (!callback && command.Length == 0) return;
        if (!callback && chatType != "private") return;
        if (callback && command.StartsWith("claim:") && chat.ToString() != settings.Telegram().ChatId) return;
        if (callback && !command.StartsWith("claim:") && (chatType != "private" || chat != sender)) return;
        try
        {
            var actor = await ActorAsync(sender, token);
            if (command is "/start" or "/my" or "Мои заявки")
            {
                if (command == "/start") await telegram.SendToAsync(sender.ToString(), "Управление заявками GLPI. «Мои заявки» — назначенные обращения, «Отмена» — отменить ввод ответа.", new { keyboard = new[] { new[] { new { text = "Мои заявки" }, new { text = "Отмена" } } }, resize_keyboard = true }, token);
                var all = await db.Tickets.Where(t => t.Status != "6").OrderByDescending(t => t.CreatedAt).ToListAsync(token);
                var mine = new List<TicketRecord>();
                foreach (var ticket in all) { await tickets.SyncAsync(ticket, token); if (TicketManagement.Status(ticket.Status) < 5 && (await glpi.AssigneesAsync(ticket.GlpiId, token)).Contains(actor.GlpiUserId)) mine.Add(ticket); }
                var rows = mine.Select(t => new[] { Button("#" + t.GlpiId + " · " + TicketManagement.StatusName(TicketManagement.Status(t.Status)), "open:" + t.Id) }).Chunk(80);
                if (mine.Count == 0) await telegram.SendToAsync(sender.ToString(), "Активных заявок на вас нет. Команда /my — мои заявки; /cancel — отмена ввода ответа.", null, token);
                else foreach (var chunk in rows) await telegram.SendToAsync(sender.ToString(), "Ваши активные заявки:", new { inline_keyboard = chunk }, token);
            }
            else if (command is "/cancel" or "Отмена")
            {
                var pending = await db.TelegramReplySessions.FindAsync([sender], token);
                if (pending != null) { db.TelegramReplySessions.Remove(pending); await db.SaveChangesAsync(token); }
                await telegram.SendToAsync(sender.ToString(), "Ввод отменён. /my — мои заявки.", null, token);
            }
            else if (callback)
            {
                string[] parts = command.Split(':');
                if (parts.Length < 2 || !int.TryParse(parts[1], out int ticketId)) throw new ArgumentException("Неизвестная команда.");
                var ticket = await db.Tickets.FindAsync([ticketId], token) ?? throw new ArgumentException("Заявка не найдена.");
                if (parts[0] == "claim")
                {
                    // Verify that private messages work before assigning the ticket.
                    await telegram.SendToAsync(sender.ToString(), "Принимаю заявку GLPI #" + ticket.GlpiId + "…", null, token);
                    await tickets.ClaimAsync(ticket, actor, token);
                    await telegram.AnswerCallbackAsync(callbackId!, "Назначено на " + actor.User.Username, token); callbackId = null;
                    if (message.TryGetProperty("message_id", out var mid)) await telegram.MarkClaimedAsync(chat, mid.GetInt64(), "Принята: " + actor.User.Username, token);
                    await CardAsync(sender, ticket, actor, token);
                }
                else if (parts[0] == "open") await CardAsync(sender, ticket, actor, token);
                else if (parts[0] is "reply" or "solve")
                {
                    await tickets.SyncAsync(ticket, token);
                    if (!(await glpi.AssigneesAsync(ticket.GlpiId, token)).Contains(actor.GlpiUserId) || TicketManagement.Status(ticket.Status) == 6) throw new UnauthorizedAccessException("Заявка недоступна для ответа.");
                    var session = await db.TelegramReplySessions.FindAsync([sender], token);
                    if (session == null) { session = new() { Id = sender }; db.TelegramReplySessions.Add(session); }
                    session.TicketId = ticket.Id; session.Mode = parts[0]; session.BotKey = Security.KeyHash(settings.Telegram().BotToken); session.ExpiresAt = DateTime.UtcNow.AddMinutes(15); await db.SaveChangesAsync(token);
                    await telegram.SendToAsync(sender.ToString(), "Напишите " + (parts[0] == "solve" ? "решение" : "ответ") + " для GLPI #" + ticket.GlpiId + ". Следующее текстовое сообщение будет сохранено в GLPI. /cancel — отмена (15 минут).", null, token);
                }
                else if (parts[0] == "status" && parts.Length == 3 && int.TryParse(parts[2], out int status)) { await tickets.ActAsync(ticket, actor, "status", "", status, token); await CardAsync(sender, ticket, actor, token); }
                else throw new ArgumentException("Неизвестная команда.");
            }
            else
            {
                var session = await db.TelegramReplySessions.FindAsync([sender], token);
                if (session == null || session.ExpiresAt < DateTime.UtcNow || session.BotKey != Security.KeyHash(settings.Telegram().BotToken)) { await telegram.SendToAsync(sender.ToString(), "Используйте /my и кнопку «Ответить» или «Добавить решение».", null, token); return; }
                var ticket = await db.Tickets.FindAsync([session.TicketId], token) ?? throw new ArgumentException("Заявка не найдена.");
                string mode = session.Mode;
                // Consume the input before the non-idempotent GLPI request: never replay a comment after a crash.
                db.TelegramReplySessions.Remove(session); await db.SaveChangesAsync(token);
                await tickets.ActAsync(ticket, actor, mode, command, 0, token);
                await telegram.SendToAsync(sender.ToString(), "Сохранено в GLPI #" + ticket.GlpiId + ".", null, token);
                await CardAsync(sender, ticket, actor, token);
            }
            if (callbackId != null) await telegram.AnswerCallbackAsync(callbackId, "Готово", token);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or InvalidOperationException or HttpRequestException)
        {
            string error = ex is HttpRequestException ? "GLPI недоступен. Проверьте заявку перед повтором действия." : ex.Message;
            if (callbackId != null) await telegram.AnswerCallbackAsync(callbackId, Limit(error)[..Math.Min(error.Length, 190)], token);
            else await telegram.SendToAsync(sender.ToString(), Limit(error), null, token);
        }
    }
}

public class TelegramBotHealth { public string LastError { get; set; } = ""; public DateTime? LastPollAt { get; set; } }
public class TelegramBotInbox(HelperDb db, TelegramBotHandler handler, ILogger<TelegramBotInbox> logger)
{
    public async Task ProcessAsync(TelegramBotState state, JsonElement updates, CancellationToken token)
    {
        foreach (var update in updates.EnumerateArray())
        {
            long id = update.GetProperty("update_id").GetInt64(); string key = state.Id + ":" + id;
            if (!await db.TelegramHandledUpdates.AnyAsync(x => x.Id == key, token))
            {
                // Persist receipt before a non-idempotent GLPI operation. Ambiguous failures require an explicit retry.
                db.TelegramHandledUpdates.Add(new() { Id = key }); await db.SaveChangesAsync(token);
                try { await handler.HandleAsync(update, token); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning("Telegram update {Id} failed: {Type}; not replayed", id, ex.GetType().Name); }
            }
            state.Offset = Math.Max(state.Offset, id + 1); await db.SaveChangesAsync(token);
        }
    }
}
public class TelegramBotWorker(IServiceScopeFactory scopes, IntegrationSettings settings, TelegramBotHealth health, ILogger<TelegramBotWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var options = settings.Telegram();
                if (options.ManagementEnabled)
                {
                    using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HelperDb>();
                    string bot = Security.KeyHash(options.BotToken);
                    var state = await db.TelegramBotStates.FindAsync([bot], token);
                    if (state == null) { state = new() { Id = bot }; db.TelegramBotStates.Add(state); await db.SaveChangesAsync(token); }
                    var client = scope.ServiceProvider.GetRequiredService<TelegramClient>();
                    var updates = await client.UpdatesAsync(state.Offset, token);
                    health.LastPollAt = DateTime.UtcNow; health.LastError = "";
                    await scope.ServiceProvider.GetRequiredService<TelegramBotInbox>().ProcessAsync(state, updates, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) { health.LastError = ex is InvalidOperationException ? ex.Message : "Не удалось обработать обновления Telegram. Проверьте журнал сервера."; logger.LogWarning("Telegram polling failed: {Type}. Check bot settings, webhook and connectivity.", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(3), token); } catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        }
    }
}

public class TicketSyncWorker(IServiceScopeFactory scopes, ILogger<TicketSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HelperDb>();
                var service = scope.ServiceProvider.GetRequiredService<TicketManagement>();
                var batch = await db.Tickets.OrderBy(t => t.SyncedAt).Take(30).ToListAsync(token);
                foreach (var ticket in batch)
                    try { await service.SyncAsync(ticket, token); } catch (Exception ex) when (ex is not OperationCanceledException) { ticket.SyncedAt = DateTime.UtcNow; await db.SaveChangesAsync(token); logger.LogWarning("GLPI synchronization failed for ticket {Id}: {Type}", ticket.GlpiId, ex.GetType().Name); }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Ticket synchronization failed: {Type}", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(60), token); } catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        }
    }
}
