using System.Text.Json;

namespace LitleHelperServer;

public sealed record SurveyPolicy
{
    public bool Mandatory { get; init; }
    public DateTime? From { get; init; }
    public int[] BranchIds { get; init; } = [];
    public int ReminderMinutes { get; init; } = 120;
    public int DailyDeferrals { get; init; } = 2;
    public int DueBusinessHours { get; init; } = 9;
    public int WorkStart { get; init; } = 9;
    public int WorkEnd { get; init; } = 18;
    public bool AdminAlertsEnabled { get; init; } = true;
    public int[] AdminUserIds { get; init; } = [];
    public int AdminRepeatMinutes { get; init; } = 240;
    public int UndeliveredBusinessHours { get; init; } = 9;
    public void Validate()
    {
        if (BranchIds == null || BranchIds.Length > 1000 || BranchIds.Any(x => x < 1)
            || AdminUserIds == null || AdminUserIds.Length > 1000 || AdminUserIds.Any(x=>x<1)
            || AdminRepeatMinutes is < 60 or > 10080 || UndeliveredBusinessHours is < 1 or > 90
            || ReminderMinutes is < 30 or > 1440 || DailyDeferrals is < 0 or > 10
            || DueBusinessHours is < 1 or > 90 || WorkStart is < 0 or > 23 || WorkEnd <= WorkStart || WorkEnd > 24)
            throw new ArgumentException("Проверьте сроки, филиалы и рабочее время опросов");
    }
    public bool Applies(ReportTicket t) => Mandatory && From != null && t.ClosedAt >= From
        && (BranchIds.Length == 0 || t.BranchId.HasValue && BranchIds.Contains(t.BranchId.Value));
    public static SurveyPolicy Read(GlpiReportState state) => JsonSerializer.Deserialize<SurveyPolicy>(state.SurveyPolicyJson) ?? new();
    public static DateOnly Day(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, ReportMath.Zone));
    public bool Working(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, ReportMath.Zone);
        return local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && local.Hour >= WorkStart && local.Hour < WorkEnd;
    }
    public DateTime AfterBusinessMinutes(DateTime utc, int minutes)
    {
        // Minute precision keeps deadlines reproducible across weekends and timezone changes.
        var at = utc;
        while (minutes > 0) { if (Working(at)) minutes--; at = at.AddMinutes(1); }
        return at;
    }
    public static bool Active(TicketSurveyInvitation i) => i.State is "Pending" or "Writing" or "Blocked";
    public static bool Eligible(TicketSurveyInvitation i, ReportTicket t) => !t.Deleted && t.Status == 6
        && t.ClosedAt == i.ClosedAt && t.Score == null && t.Requesters.FirstOrDefault()?.Id == i.RequesterId
        && !t.Assignees.Any(a => a.Id == i.RequesterId) && t.Responsible?.Id != i.RequesterId;
    public bool Required(TicketSurveyInvitation i, DateTime now) => Mandatory && i.Mandatory && i.State == "Pending"
        && i.ExpiresAt > now && i.FirstShownAt != null && i.NextNoticeAt <= now
        && (i.DueAt <= now || DailyDeferrals == 0 || i.DeferralDay == Day(now).ToString("yyyy-MM-dd") && i.Deferrals >= DailyDeferrals);
}
