namespace LitleHelperServer;

public sealed record CodeWhiteAlertData(
    bool Active,
    string Cabinet,
    string BranchName,
    string Time,
    DateTime? ActivatedAtUtc = null,
    int RemainingSeconds = 0
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

    public static void TriggerAlert(string cabinet, string branchName, string time)
    {
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
                (int)AutoResetDuration.TotalSeconds
            );
            data = currentState;
            autoResetTimer = new Timer(_ => ResetAlert(), null, AutoResetDuration, Timeout.InfiniteTimeSpan);
        }
        OnAlert?.Invoke(data);
    }

    public static void ResetAlert()
    {
        lock (sync)
        {
            autoResetTimer?.Dispose();
            autoResetTimer = null;
            currentState = new CodeWhiteAlertData(false, "", "", "", null, 0);
        }
        OnReset?.Invoke();
    }
}
