using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LitleHelperServer;
public class TaskExpiryService(IServiceScopeFactory scopes, IHubContext<HelperHub> hub, ILogger<TaskExpiryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HelperDb>();
                    DateTime limit = DateTime.UtcNow.AddMinutes(-5);
                    var expired = await db.AuditLogs.Where(l => l.Status == "Pending" && l.Timestamp < limit).ToListAsync(stoppingToken);
                    foreach (var log in expired)
                    {
                        log.Status = "TimedOut";
                        log.Result += "\nСервер не получил итоговый ответ в течение 5 минут.";
                        if (log.Result.Length > 131072) log.Result = log.Result[..131072];
                    }
                    await db.SaveChangesAsync(stoppingToken);
                    foreach (var log in expired) await hub.Clients.Group("Admin:" + log.AdminUsername).SendAsync("ExecutionResult", log, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Command timeout sweep failed"); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
