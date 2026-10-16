namespace LitleHelperServer;

public sealed record CodeWhiteAlertData(
    bool Active,
    string Cabinet,
    string BranchName,
    string Time,
    DateTime? ActivatedAtUtc = null,
    int RemainingSeconds = 0,
    bool Accepted = false,
    string? EventId = null
);

public static class GuardWhiteService
{
    private static readonly object sync = new();
    private static CodeWhiteAlertData currentState = new(false, "", "", "", null, 0);
    private static Timer? autoResetTimer;
    public static readonly TimeSpan AutoResetDuration = TimeSpan.FromMinutes(10);

    public static event Action<CodeWhiteAlertData>? OnAlert;
    public static event Action? OnReset;

    public static CodeWhiteAlertData GetState()
    {
        lock (sync)
        {
            if (currentState.Active && currentState.ActivatedAtUtc.HasValue)
            {
                var elapsed = DateTime.UtcNow - currentState.ActivatedAtUtc.Value;
                if (elapsed >= AutoResetDuration)
                {
                    autoResetTimer?.Dispose();
                    autoResetTimer = null;
                    currentState = new CodeWhiteAlertData(false, "", "", "", null, 0);
                }
                else
                {
                    int rem = Math.Max(0, (int)(AutoResetDuration - elapsed).TotalSeconds);
                    return currentState with { RemainingSeconds = rem };
                }
            }
            return currentState;
        }
    }

    public static void TriggerAlert(string cabinet, string branchName, string time, string? eventId = null)
    {
        eventId ??= Guid.NewGuid().ToString("N");
        CodeWhiteAlertData data;
        lock (sync)
        {
            autoResetTimer?.Dispose();
            var now = DateTime.UtcNow;
            currentState = new CodeWhiteAlertData(
                true,
                cabinet ?? "Не указан",
                branchName ?? "Главный корпус",
                time ?? DateTime.Now.ToString("HH:mm:ss"),
                now,
                (int)AutoResetDuration.TotalSeconds,
                false,eventId
            );
            data = currentState;
            autoResetTimer = new Timer(_ => ResetAlert(eventId), null, AutoResetDuration, Timeout.InfiniteTimeSpan);
        }
        OnAlert?.Invoke(data);
    }

    public static void AcknowledgeAlert()
    {
        CodeWhiteAlertData data;
        lock(sync) { if(!currentState.Active)return; currentState=currentState with {Accepted=true};data=currentState; }
        OnAlert?.Invoke(data);
    }

    public static void ResetAlert(string? expectedEventId = null)
    {
        lock (sync)
        {
            if(expectedEventId!=null && currentState.EventId!=expectedEventId)return;
            autoResetTimer?.Dispose();
            autoResetTimer = null;
            currentState = new CodeWhiteAlertData(false, "", "", "", null, 0);
        }
        OnReset?.Invoke();
    }
}
