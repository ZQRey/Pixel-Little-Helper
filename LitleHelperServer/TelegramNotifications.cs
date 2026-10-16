using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LitleHelperServer;

public class TelegramDelivery
{
    public int TicketId { get; set; }
    public TicketRecord Ticket { get; set; } = null!;
    public int Attempts { get; set; }
    public string State { get; set; } = "Pending";
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public string LastError { get; set; } = "";
    public long GroupMessageId { get; set; }
    public string GroupChatId { get; set; } = "";
    public string ButtonState { get; set; } = "";
}
public class TelegramClient(HttpClient http, IntegrationSettings settings, IConfiguration configuration)
{
    public async Task SendAsync(string text, CancellationToken token)
        => await SendToAsync(settings.Telegram().ChatId, text, null, token, settings.Telegram().ThreadId);
    public async Task SendTicketAsync(TicketRecord ticket, CancellationToken token)
    { await SendTicketTrackedAsync(ticket,token); }
    public async Task<long> SendTicketTrackedAsync(TicketRecord ticket, CancellationToken token)
    {
        var options = settings.Telegram();
        object? keyboard = options.ManagementEnabled ? TicketKeyboard(ticket) : null;
        var body=new Dictionary<string,object>{["chat_id"]=options.ChatId,["text"]=Message(ticket),["link_preview_options"]=new {is_disabled=true}};
        if(options.ThreadId>0)body["message_thread_id"]=options.ThreadId;if(keyboard!=null)body["reply_markup"]=keyboard;
        var result=await CallAsync("sendMessage",body,token);return result.TryGetProperty("message_id",out var id)?id.GetInt64():0;
    }
    public static string TicketButtonState(TicketRecord ticket)
    {
        if (TicketManagement.Status(ticket.Status) >= 5) return "closed:" + ticket.Id + ":" + ticket.Status;
        if (ticket.AssignedGlpiUserId > 0) return "claimed:" + ticket.Id + ":" + (ticket.AssignedUsername ?? "");
        return "claim:" + ticket.Id;
    }
    public static string TicketButtonText(TicketRecord ticket)
    {
        if (TicketManagement.Status(ticket.Status) >= 5) return "Заявка закрыта";
        if (ticket.AssignedGlpiUserId > 0)
        {
            string name = !string.IsNullOrWhiteSpace(ticket.AssignedUsername) ? ticket.AssignedUsername.Trim() : "Специалист";
            if (name.Length > 28) name = name[..27] + "…";
            return "Взял: " + name;
        }
        return "Принять";
    }
    public static string TicketButtonCallbackData(TicketRecord ticket)
    {
        if (TicketManagement.Status(ticket.Status) >= 5) return "closed:" + ticket.Id;
        if (ticket.AssignedGlpiUserId > 0) return "claimed:" + ticket.Id;
        return "claim:" + ticket.Id;
    }
    private static object TicketKeyboard(TicketRecord ticket) =>
        new { inline_keyboard = new[] { new[] { new { text = TicketButtonText(ticket), callback_data = TicketButtonCallbackData(ticket) } } } };
    public async Task SyncTicketButtonAsync(TicketRecord ticket,TelegramDelivery delivery,CancellationToken token)
    {
        if(delivery.GroupMessageId==0 || delivery.GroupChatId.Length==0 || !settings.Telegram().ManagementEnabled || delivery.ButtonState==TicketButtonState(ticket))return;
        await CallAsync("editMessageReplyMarkup",new {chat_id=delivery.GroupChatId,message_id=delivery.GroupMessageId,reply_markup=TicketKeyboard(ticket)},token);delivery.ButtonState=TicketButtonState(ticket);
    }
    public async Task SendToAsync(string chat, string text, object? keyboard, CancellationToken token, int thread = 0)
    {
        var options = settings.Telegram();
        if (options.BotToken.Length == 0 || chat.Length == 0) throw new InvalidOperationException("Сначала укажите токен бота и Chat ID.");
        var body = new Dictionary<string, object> { ["chat_id"] = chat, ["text"] = text, ["link_preview_options"] = new { is_disabled = true } };
        if (thread > 0) body["message_thread_id"] = thread;
        if (keyboard != null) body["reply_markup"] = keyboard;
        await CallAsync("sendMessage", body, token);
    }
    public async Task SendEmergencyAlertAsync(string title, string code, string cabinet, string? branchName, string? initiator, string? notes, string? department, CancellationToken token = default)
    {
        var em = settings.Emergency();
        var tg = settings.Telegram();
        if (!tg.Enabled || !em.TelegramAlertsEnabled) return;
        string targetChat = !string.IsNullOrWhiteSpace(em.TelegramChatId) ? em.TelegramChatId.Trim() : tg.ChatId;
        int targetThread = em.TelegramThreadId > 0 ? em.TelegramThreadId : tg.ThreadId;
        if (string.IsNullOrWhiteSpace(targetChat)) return;

        string header = code == "CODE_BLUE" ? "🆘 СРОЧНАЯ РЕАНИМАЦИЯ: КОД СИНИЙ!" : $"🚨 ЭКСТРЕННЫЙ ВЫЗОВ: {title}";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(header);
        sb.AppendLine($"Код: {code}");
        if (!string.IsNullOrWhiteSpace(branchName)) sb.AppendLine($"Филиал: {branchName}");
        sb.AppendLine($"Кабинет: {cabinet}");
        if (!string.IsNullOrWhiteSpace(initiator)) sb.AppendLine($"Инициатор: {initiator}");
        if (!string.IsNullOrWhiteSpace(department)) sb.AppendLine($"Направление: {department}");
        if (!string.IsNullOrWhiteSpace(notes)) sb.AppendLine($"Примечание: {notes}");
        sb.AppendLine($"Время: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        await SendToAsync(targetChat, sb.ToString(), null, token, targetThread);
    }

    public async Task SendAttachmentAsync(string chatId, string fileName, byte[] content, long replyToMessageId, int threadId, CancellationToken token)
    {
        var options = settings.Telegram();
        if (string.IsNullOrWhiteSpace(options.BotToken) || string.IsNullOrWhiteSpace(chatId)) return;

        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        bool isPhoto = ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp";
        string method = isPhoto ? "sendPhoto" : "sendDocument";
        string fieldName = isPhoto ? "photo" : "document";

        string apiBase = configuration["Telegram:ApiBaseUrl"] ?? "https://api.telegram.org";
        string url = apiBase.TrimEnd('/') + "/bot" + options.BotToken + "/" + method;

        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(chatId), "chat_id");
            if (threadId > 0) form.Add(new StringContent(threadId.ToString()), "message_thread_id");
            if (replyToMessageId > 0) form.Add(new StringContent(replyToMessageId.ToString()), "reply_to_message_id");

            var fileContent = new ByteArrayContent(content);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, fieldName, fileName);

            using var response = await http.PostAsync(url, form, token);
            if (!response.IsSuccessStatusCode && isPhoto)
            {
                using var docForm = new MultipartFormDataContent();
                docForm.Add(new StringContent(chatId), "chat_id");
                if (threadId > 0) docForm.Add(new StringContent(threadId.ToString()), "message_thread_id");
                if (replyToMessageId > 0) docForm.Add(new StringContent(replyToMessageId.ToString()), "reply_to_message_id");
                var docContent = new ByteArrayContent(content);
                docContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                docForm.Add(docContent, "document", fileName);
                await http.PostAsync(apiBase.TrimEnd('/') + "/bot" + options.BotToken + "/sendDocument", docForm, token);
            }
        }
        catch { }
    }

    public async Task<JsonElement> UpdatesAsync(long offset, CancellationToken token) =>
        await CallAsync("getUpdates", new { offset, timeout = 0, limit = 20, allowed_updates = new[] { "message", "callback_query" } }, token);
    public async Task AnswerCallbackAsync(string id, string text, CancellationToken token, bool showAlert = false) =>
        await CallAsync("answerCallbackQuery", new { callback_query_id = id, text, show_alert = showAlert }, token);
    public async Task MarkClaimedAsync(long chat, long messageId, string text, string callbackData, CancellationToken token)
    {
        try { await CallAsync("editMessageReplyMarkup", new { chat_id = chat, message_id = messageId, reply_markup = new { inline_keyboard = new[] { new[] { new { text, callback_data = callbackData } } } } }, token); }
        catch (InvalidOperationException) { /* Assignment already succeeded; a stale group button cannot reassign the ticket. */ }
    }
    public async Task MarkClaimedAsync(long chat, long messageId, string text, CancellationToken token) =>
        await MarkClaimedAsync(chat, messageId, text, "claimed", token);
    private async Task<JsonElement> CallAsync(string method, object body, CancellationToken token)
    {
        var options = settings.Telegram();
        try
        {
            // The token is part of the URI: HTTP logging is disabled for this client.
            string apiBase = configuration["Telegram:ApiBaseUrl"] ?? "https://api.telegram.org";
            using var response = await http.PostAsJsonAsync(apiBase.TrimEnd('/') + "/bot" + options.BotToken + "/" + method, body, token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException((int)response.StatusCode switch
            {
                401 => "Telegram: неверный токен бота.", 403 => "Telegram: бот не может писать в эту группу. Проверьте участие и права.",
                400 => "Telegram: проверьте Chat ID, ID темы и доступ бота к группе.", 409 => "Telegram: отключите webhook и другие программы getUpdates для этого бота.", 429 => "Telegram: превышен лимит отправки. Отправка будет повторена.",
                _ => "Telegram временно недоступен."
            });
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (!json.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) throw new InvalidOperationException("Telegram не подтвердил отправку сообщения.");
            return json.RootElement.TryGetProperty("result", out var result) ? result.Clone() : json.RootElement.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { throw new InvalidOperationException("Не удалось связаться с Telegram. Проверьте доступ сервера к api.telegram.org."); }
    }
    public static string Message(TicketRecord ticket)
    {
        string userDisplay = string.IsNullOrWhiteSpace(ticket.UserFullName)
            ? ticket.Username
            : (string.IsNullOrWhiteSpace(ticket.Username) || ticket.Username.Equals(ticket.UserFullName, StringComparison.OrdinalIgnoreCase)
                ? ticket.UserFullName
                : $"{ticket.UserFullName} ({ticket.Username})");
        string text = $"Новая заявка GLPI #{ticket.GlpiId}\nПользователь: {userDisplay}\nКомпьютер: {ticket.MachineName}\nФилиал: {(ticket.BranchName.Length > 0 ? ticket.BranchName : "Не указан")}\nКабинет: {(ticket.Room.Length > 0 ? ticket.Room : "Не указан")}\n{ticket.Title}\n\n{ticket.Description}";
        // Leave room below Telegram's 4096-character limit; preserve UTF-16 pairs.
        if (text.Length > 3800) { int end = char.IsHighSurrogate(text[3799]) ? 3799 : 3800; text = text[..end] + "\n… Полный текст в веб-панели."; }
        return text;
    }
}
public class TelegramWorker(IServiceScopeFactory scopes, IntegrationSettings settings, ILogger<TelegramWorker> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (settings.Telegram().Enabled)
                {
                    using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HelperDb>();
                    var due = await db.TelegramDeliveries.Include(x => x.Ticket).Where(x => x.State == "Pending" && x.NextAttemptAt <= DateTime.UtcNow).OrderBy(x => x.NextAttemptAt).Take(20).ToListAsync(stoppingToken);
                    var sender = scope.ServiceProvider.GetRequiredService<TelegramClient>();
                    foreach (var item in due)
                    {
                        item.Attempts++;
                        try
                        {
                            item.GroupMessageId = await sender.SendTicketTrackedAsync(item.Ticket, stoppingToken);
                            item.GroupChatId = settings.Telegram().ChatId;
                            item.ButtonState = TelegramClient.TicketButtonState(item.Ticket);
                            item.State = "Sent";
                            item.SentAt = DateTime.UtcNow;
                            item.LastError = "";

                            if (item.GroupMessageId > 0)
                            {
                                var attachments = await db.TicketAttachments.AsNoTracking().Where(a => a.TicketId == item.TicketId).ToListAsync(stoppingToken);
                                foreach (var att in attachments)
                                {
                                    string filePath = Path.Combine(TicketAttachments.Folder(configuration), att.StorageKey);
                                    if (File.Exists(filePath))
                                    {
                                        byte[] bytes = await File.ReadAllBytesAsync(filePath, stoppingToken);
                                        await sender.SendAttachmentAsync(settings.Telegram().ChatId, att.FileName, bytes, item.GroupMessageId, settings.Telegram().ThreadId, stoppingToken);
                                    }
                                }
                            }
                        }
                        catch (InvalidOperationException ex)
                        {
                            item.LastError = ex.Message; item.State = item.Attempts >= 10 ? "Failed" : "Pending";
                            item.NextAttemptAt = DateTime.UtcNow.AddSeconds(Math.Min(3600, 10 * Math.Pow(2, item.Attempts)));
                        }
                        await db.SaveChangesAsync(stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch { logger.LogWarning("Не удалось обработать очередь Telegram. Обработка будет повторена."); }
            try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
