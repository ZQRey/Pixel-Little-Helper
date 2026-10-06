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
    {
        var options = settings.Telegram();
        if (options.BotToken.Length == 0 || options.ChatId.Length == 0) throw new InvalidOperationException("Сначала укажите токен бота и Chat ID.");
        var body = new Dictionary<string, object> { ["chat_id"] = options.ChatId, ["text"] = text, ["link_preview_options"] = new { is_disabled = true } };
        if (options.ThreadId > 0) body["message_thread_id"] = options.ThreadId;
        try
        {
            // The token is part of the URI: HTTP logging is disabled for this client.
            string apiBase = configuration["Telegram:ApiBaseUrl"] ?? "https://api.telegram.org";
            using var response = await http.PostAsJsonAsync(apiBase.TrimEnd('/') + "/bot" + options.BotToken + "/sendMessage", body, token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException((int)response.StatusCode switch
            {
                401 => "Telegram: неверный токен бота.", 403 => "Telegram: бот не может писать в эту группу. Проверьте участие и права.",
                400 => "Telegram: проверьте Chat ID, ID темы и доступ бота к группе.", 429 => "Telegram: превышен лимит отправки. Отправка будет повторена.",
                _ => "Telegram временно недоступен."
            });
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (!json.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) throw new InvalidOperationException("Telegram не подтвердил отправку сообщения.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { throw new InvalidOperationException("Не удалось связаться с Telegram. Проверьте доступ сервера к api.telegram.org."); }
    }
    public static string Message(TicketRecord ticket)
    {
        string text = $"Новая заявка GLPI #{ticket.GlpiId}\nПользователь: {ticket.Username}\nКомпьютер: {ticket.MachineName}\n{ticket.Title}\n\n{ticket.Description}";
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
                        try { await sender.SendAsync(TelegramClient.Message(item.Ticket), stoppingToken); item.State = "Sent"; item.SentAt = DateTime.UtcNow; item.LastError = ""; }
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
