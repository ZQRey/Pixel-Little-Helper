using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LitleHelperServer;
public record ChatOptions(bool Enabled = true, int RetentionDays = 0);
public class MessengerSettings
{
    private readonly object gate = new();
    private readonly string file;
    private ChatOptions options = new();
    public MessengerSettings(IConfiguration config) { file = config["Messenger:SettingsFile"] ?? Path.Combine("data", "messenger.json"); if (File.Exists(file)) options = JsonSerializer.Deserialize<ChatOptions>(File.ReadAllText(file)) ?? new(); }
    public ChatOptions Value { get { lock (gate) return options; } }
    public void Save(ChatOptions request)
    {
        if (request.RetentionDays != 0 && request.RetentionDays is < 30 or > 3650) throw new ArgumentException("Хранение: 0 (без ограничения) либо от 30 до 3650 дней.");
        lock (gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(request)); File.Move(file + ".tmp", file, true); options = request;
        }
    }
}
public class ChatRetentionWorker(IServiceScopeFactory scopes, MessengerSettings settings, ILogger<ChatRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int days = settings.Value.RetentionDays;
                if (days > 0)
                {
                    using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HelperDb>(); var cutoff = DateTime.UtcNow.AddDays(-days);
                    int count = await db.ChatMessages.Where(m => m.SentAt < cutoff).ExecuteDeleteAsync(stoppingToken);
                    if (count > 0) logger.LogInformation("Messenger retention removed {Count} messages", count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Messenger retention failed"); }
            try { await Task.Delay(TimeSpan.FromHours(24), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
