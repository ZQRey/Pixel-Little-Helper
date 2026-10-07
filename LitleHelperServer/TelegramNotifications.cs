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
}
public class TelegramClient(HttpClient http, IntegrationSettings settings, IConfiguration configuration)
{
    public async Task SendAsync(string text, CancellationToken token)
        => await SendToAsync(settings.Telegram().ChatId, text, null, token, settings.Telegram().ThreadId);
    public async Task SendTicketAsync(TicketRecord ticket, CancellationToken token)
    {
        var options = settings.Telegram();
        object? keyboard = options.ManagementEnabled ? new { inline_keyboard = new[] { new[] { new { text = "Принять", callback_data = "claim:" + ticket.Id } } } } : null;
        await SendToAsync(options.ChatId, Message(ticket), keyboard, token, options.ThreadId);
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
    public async Task<JsonElement> UpdatesAsync(long offset, CancellationToken token) =>
        await CallAsync("getUpdates", new { offset, timeout = 0, limit = 20, allowed_updates = new[] { "message", "callback_query" } }, token);
    public async Task AnswerCallbackAsync(string id, string text, CancellationToken token) =>
        await CallAsync("answerCallbackQuery", new { callback_query_id = id, text, show_alert = false }, token);
    public async Task MarkClaimedAsync(long chat, long messageId, string text, CancellationToken token)
    {
        try { await CallAsync("editMessageReplyMarkup", new { chat_id = chat, message_id = messageId, reply_markup = new { inline_keyboard = new[] { new[] { new { text, callback_data = "claimed" } } } } }, token); }
        catch (InvalidOperationException) { /* Assignment already succeeded; a stale group button cannot reassign the ticket. */ }
    }
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
        string text = $"Новая заявка GLPI #{ticket.GlpiId}\nПользователь: {ticket.Username}\nКомпьютер: {ticket.MachineName}\nФилиал: {(ticket.BranchName.Length > 0 ? ticket.BranchName : "Не указан")}\nКабинет: {(ticket.Room.Length > 0 ? ticket.Room : "Не указан")}\n{ticket.Title}\n\n{ticket.Description}";
        // Leave room below Telegram's 4096-character limit; preserve UTF-16 pairs.
        if (text.Length > 3800) { int end = char.IsHighSurrogate(text[3799]) ? 3799 : 3800; text = text[..end] + "\n… Полный текст в веб-панели."; }
        return text;
    }
}
public class TelegramWorker(IServiceScopeFactory scopes, IntegrationSettings settings, ILogger<TelegramWorker> logger) : BackgroundService
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
                        try { await sender.SendTicketAsync(item.Ticket, stoppingToken); item.State = "Sent"; item.SentAt = DateTime.UtcNow; item.LastError = ""; }
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
