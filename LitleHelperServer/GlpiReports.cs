using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace LitleHelperServer;

// A complete, atomically replaced projection. Operational Tickets remain untouched.
public class GlpiReportState
{
    [Key] public string Id { get; set; } = "";
    public string Payload { get; set; } = "[]";
    public DateTime? SyncedAt { get; set; }
    public DateTime? SurveysFrom { get; set; }
    public bool SurveysEnabled { get; set; }
    public string SurveyPolicyJson { get; set; } = "{}";
}
public class TicketSurveyInvitation
{
    [Key] public string Id { get; set; } = "";
    public string Source { get; set; } = "";
    public int TicketId { get; set; }
    public int RequesterId { get; set; }
    public string Owner { get; set; } = "";
    public int ResponsibleId { get; set; }
    public string ResponsibleName { get; set; } = "";
    public DateTime ClosedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime NextNoticeAt { get; set; }
    public DateTime? AnsweredAt { get; set; }
    public string State { get; set; } = "Pending";
    public int Notices { get; set; }
    public string AnswerRequestId { get; set; } = "";
    public string AnswerHash { get; set; } = "";
    public DateTime? LastSentAt { get; set; }
    public bool Mandatory { get; set; }
    public DateTime? FirstShownAt { get; set; }
    public DateTime? LastShownAt { get; set; }
    public DateTime? DueAt { get; set; }
    public string DeferralDay { get; set; } = "";
    public int Deferrals { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public string LeaseOwner { get; set; } = "";
    public int PendingScore { get; set; }
    public string PendingComment { get; set; } = "";
    public string PendingRequestId { get; set; } = "";
    public DateTime? NextWriteAt { get; set; }
    public int WriteAttempts { get; set; }
    public string LastError { get; set; } = "";
}
public record ReportActor(int Id, string Login, string Name, string Profile = "", bool IsService = false);
public record ReportTicket
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public string Url { get; init; } = "";
    public int EntityId { get; init; }
    public int? BranchId { get; init; }
    public string Branch { get; init; } = "Не сопоставлен";
    public string Category { get; init; } = "Не указана";
    public int Priority { get; init; }
    public int Status { get; init; }
    public bool Deleted { get; init; }
    public DateTime? CreatedAt { get; init; }
    public DateTime? AcceptedAt { get; init; }
    public DateTime? SolvedAt { get; init; }
    public DateTime? ClosedAt { get; init; }
    public double? EffortSeconds { get; init; }
    public int WaitingSeconds { get; init; }
    public ReportActor[] Requesters { get; init; } = [];
    public ReportActor[] Assignees { get; init; } = [];
    public ReportActor? RegisteredBy { get; init; }
    public ReportActor? Responsible { get; init; }
    public string Attribution { get; init; } = "Не определён";
    public double? Score { get; init; }
    public DateTime? AnsweredAt { get; init; }
    public double? CurrentScore => Status == 6 && ClosedAt != null && AnsweredAt >= ClosedAt ? Score : null;
    public double? ResolutionSeconds => Duration(CreatedAt, SolvedAt);
    public double? AcceptanceSeconds => Duration(CreatedAt, AcceptedAt);
    public double? ClosureSeconds => Duration(CreatedAt, ClosedAt);
    public double? ExecutionSeconds => Status == 6 ? Duration(AcceptedAt, ClosedAt) : null;
    public double? LifetimeSeconds => Status == 6 ? Duration(CreatedAt, ClosedAt) : null;
    public static double? Duration(DateTime? start, DateTime? end) => start.HasValue && end >= start ? (end.Value - start.Value).TotalSeconds : null;
}
public sealed class ReportGate { public SemaphoreSlim Sync { get; } = new(1, 1); public SemaphoreSlim Surveys { get; } = new(1, 1); public SemaphoreSlim Alerts { get; } = new(1, 1); }
public sealed class ReportHealth { public DateTime? LastAttempt { get; set; } public string Error { get; set; } = ""; }
public record ReportFilter(DateOnly From, DateOnly To, string Basis, int? BranchId, int? Technician, int? Requester, int? Status, string Search, string? Snapshot)
{
    public int? Reaction { get; init; }
    public static ReportFilter Parse(IQueryCollection q)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ReportMath.Zone));
        DateOnly Read(string key, DateOnly fallback) => string.IsNullOrEmpty(q[key]) ? fallback : DateOnly.TryParseExact(q[key], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : throw new ArgumentException("Неверная дата");
        int? Number(string key) => string.IsNullOrEmpty(q[key]) ? null : int.TryParse(q[key], out int n) && n >= 0 ? n : throw new ArgumentException("Неверный фильтр");
        var from = Read("from", new DateOnly(today.Year, today.Month, 1)); var to = Read("to", today);
        string basis = q["basis"].FirstOrDefault() ?? "created";
        if (to < from || to == DateOnly.MaxValue || to.DayNumber - from.DayNumber > 3660 || basis is not ("created" or "solved" or "closed" or "answered")) throw new ArgumentException("Неверный период или основа даты");
        var status = Number("status"); if (status is not null && status is < 1 or > 6) throw new ArgumentException("Неверный статус");
        var reaction = Number("reaction"); if (reaction is < 0 or > 5) throw new ArgumentException("Неверный интервал времени реакции");
        return new(from, to, basis, Number("branchId"), Number("technician"), Number("requester"), status, q["search"].ToString().Trim()[..Math.Min(q["search"].ToString().Trim().Length, 200)], q["snapshot"].FirstOrDefault()) { Reaction = reaction };
    }
    public DateTime Start => TimeZoneInfo.ConvertTimeToUtc(From.ToDateTime(TimeOnly.MinValue), ReportMath.Zone);
    public DateTime End => TimeZoneInfo.ConvertTimeToUtc(To.AddDays(1).ToDateTime(TimeOnly.MinValue), ReportMath.Zone);
    public DateTime? Date(ReportTicket t) => Basis switch { "solved" => t.SolvedAt, "closed" => t.ClosedAt, "answered" => t.AnsweredAt, _ => t.CreatedAt };
    public bool Matches(ReportTicket t) => !t.Deleted && Date(t) >= Start && Date(t) < End && (Reaction == null || GlpiReportReaction.Bucket(t.AcceptanceSeconds) == Reaction) &&
        (BranchId == null || t.BranchId == BranchId) && (Technician == null || t.Responsible?.Id == Technician || t.Assignees.Any(a => a.Id == Technician)) &&
        (Requester == null || t.Requesters.Any(a => a.Id == Requester)) && (Status == null || t.Status == Status) &&
        (Search.Length == 0 || t.Title.Contains(Search, StringComparison.OrdinalIgnoreCase) || t.Id.ToString().Contains(Search, StringComparison.Ordinal));
}
public static class ReportMath
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Qyzylorda");
    public static DateTime? ParseSourceDate(string? value, TimeZoneInfo sourceZone)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), sourceZone);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var offset) ? offset.UtcDateTime : null;
    }
    public static double? Mean(IEnumerable<double?> values) { var a = values.Where(x => x.HasValue).Select(x => x!.Value).ToArray(); return a.Length == 0 ? null : a.Average(); }
    public static double? Percentile(IEnumerable<double?> values, double p)
    {
        var a = values.Where(x => x.HasValue).Select(x => x!.Value).Order().ToArray();
        if (a.Length == 0) return null; double i = (a.Length - 1) * p; int lo = (int)i; return a[lo] + (a[(int)Math.Ceiling(i)] - a[lo]) * (i - lo);
    }
    public static object Summary(ReportTicket[] rows) => new {
        total = rows.Length, open = rows.Count(t => t.Status < 5), solved = rows.Count(t => t.SolvedAt != null), closed = rows.Count(t => t.Status == 6),
        meanSeconds = Mean(rows.Select(t => t.ExecutionSeconds)), medianSeconds = Percentile(rows.Select(t => t.ExecutionSeconds), .5), p90Seconds = Percentile(rows.Select(t => t.ExecutionSeconds), .9),
        executionSamples = rows.Count(t => t.ExecutionSeconds != null),
        resolutionSamples = rows.Count(t => t.ResolutionSeconds != null), meanAcceptanceSeconds = Mean(rows.Select(t => t.AcceptanceSeconds)), meanClosureSeconds = Mean(rows.Select(t => t.ClosureSeconds)),
        effortSeconds = rows.Any(t => t.EffortSeconds.HasValue) ? rows.Sum(t => t.EffortSeconds ?? 0) : (double?)null,
        effortSamples = rows.Count(t => t.EffortSeconds.HasValue), meanScore = Mean(rows.Where(t => t.Status == 6).Select(t => t.CurrentScore)), rated = rows.Count(t => t.Status == 6 && t.CurrentScore != null),
        missingRequester = rows.Count(t => t.Requesters.Length == 0), ambiguousResponsible = rows.Count(t => t.Responsible == null), missingCreated = rows.Count(t => t.CreatedAt == null)
    };
    public static bool IsTechnician(ReportActor a) => !a.IsService && !a.Login.Equals("Telegram", StringComparison.OrdinalIgnoreCase) && a.Profile.Split(", ").Any(p => p is "Technician" or "Super-Admin");
    public static object[] People(ReportTicket[] rows, bool technicians) => rows.SelectMany(t => (technicians ? t.Assignees.Concat(t.Responsible is null ? [] : new[] { t.Responsible! }).Where(IsTechnician) : t.Requesters.Take(1)).DistinctBy(a => a.Id).Select(a => (Actor: a, Ticket: t)))
        .GroupBy(x => x.Actor.Id).Select(g => {
            var actor = g.First().Actor; var tickets = g.Select(x => x.Ticket).DistinctBy(t => t.Id).ToArray();
            var results = technicians ? tickets.Where(t => t.Responsible?.Id == actor.Id).ToArray() : tickets;
            var closed = results.Where(t => t.Status == 6).ToArray(); var scores = closed.Where(t => t.CurrentScore != null).ToArray();
            return (object)new { actor.Id, actor.Login, actor.Name, actor.Profile, total = tickets.Length, responsible = results.Length, closed = closed.Length,
                meanSeconds = Mean(results.Select(t => t.ExecutionSeconds)), samples = results.Count(t => t.ExecutionSeconds != null), rated = scores.Length,
                meanScore = Mean(scores.Select(t => t.CurrentScore)), responsePercent = closed.Length == 0 ? (double?)null : 100d * scores.Length / closed.Length,
                sufficient = scores.Length >= 5, high = scores.Count(t => t.CurrentScore >= 4), low = scores.Count(t => t.CurrentScore <= 2) };
        }).ToArray();
}

public sealed class GlpiReports(HelperDb db, GlpiService glpi, IntegrationSettings settings, ReportGate gate, ReportHealth health)
{
    public string Source => glpi.ImportSource;
    public async Task<GlpiReportState> State(CancellationToken ct)
    {
        var state = await db.GlpiReportStates.SingleOrDefaultAsync(x => x.Id == Source, ct);
        if (state == null) { await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"GlpiReportStates\" (\"Id\", \"Payload\", \"SurveysEnabled\") VALUES ({Source}, '[]', FALSE) ON CONFLICT (\"Id\") DO NOTHING", ct); state = await db.GlpiReportStates.SingleAsync(x => x.Id == Source, ct); }
        return state;
    }
    public async Task Sync(CancellationToken ct)
    {
        await gate.Sync.WaitAsync(ct);
        try
        {
            health.LastAttempt = DateTime.UtcNow;
            var rows = await glpi.ReportSnapshotAsync(settings.GlpiImport().LocationBranches ?? new(), await db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name, ct), ct);
            // Use operational branch only for an exact ID match; never infer from a display name.
            var local = await db.Tickets.AsNoTracking().Where(t => t.BranchId != null).GroupBy(t => t.GlpiId).Select(g => g.First()).ToListAsync(ct);
            rows = rows.Select(t => { var found = local.FirstOrDefault(l => l.GlpiId == t.Id); return t.BranchId == null && found != null ? t with { BranchId = found.BranchId, Branch = found.BranchName } : t; }).ToArray();
            var state = await State(ct);
            // Keep a previously established responsibility after reassignment of a closed ticket.
            var old = JsonSerializer.Deserialize<ReportTicket[]>(state.Payload) ?? [];
            rows = rows.Select(t => { var prior = old.FirstOrDefault(x => x.Id == t.Id); return t.Status == 6 && prior?.Status == 6 && t.ClosedAt == prior.ClosedAt && prior.Responsible != null && glpi.ReportTechnician(prior.Responsible) ? t with { Responsible = prior.Responsible, Attribution = prior.Attribution } : t; }).ToArray();
            state.Payload = JsonSerializer.Serialize(rows); state.SyncedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct); health.Error = "";
            await ReconcileInvitations(state, rows, ct);
        }
        finally { gate.Sync.Release(); }
    }
    public async Task ReconcileInvitations(GlpiReportState state, ReportTicket[] rows, CancellationToken ct)
    {
        await gate.Surveys.WaitAsync(ct);
        try
        {
            var invitations = await db.TicketSurveyInvitations.Where(i => i.Source == Source).ToListAsync(ct);
            var now = DateTime.UtcNow;
            var policy = SurveyPolicy.Read(state);
            foreach (var invite in invitations.Where(SurveyPolicy.Active))
            {
                var t = rows.SingleOrDefault(t => t.Id == invite.TicketId);
                if (t == null || t.Deleted || t.Status != 6 || t.ClosedAt != invite.ClosedAt || t.Requesters.FirstOrDefault()?.Id != invite.RequesterId || t.Assignees.Any(a => a.Id == invite.RequesterId) || t.Responsible?.Id == invite.RequesterId) invite.State = "Cancelled";
                else if (t.Score != null) { invite.State = "Answered"; invite.AnsweredAt = t.AnsweredAt; invite.PendingRequestId=""; invite.AnswerHash=""; }
                else if (now >= invite.ExpiresAt) { invite.State = "Expired";invite.LastError="Истекли 14 дней после закрытия заявки"; }
                else { invite.ResponsibleId = t.Responsible?.Id ?? 0; invite.ResponsibleName = t.Responsible?.Name ?? "Команда поддержки"; }
                if (!SurveyPolicy.Active(invite)) invite.PendingComment="";
            }
            if (state.SurveysEnabled && state.SurveysFrom != null)
                foreach (var t in rows.Where(t => !t.Deleted && t.Status == 6 && t.ClosedAt >= state.SurveysFrom && t.Score == null && t.Requesters.Length > 0))
                {
                    // GLPI has one survey per ticket. Never invite a second cycle automatically.
                    if (invitations.Any(i => i.TicketId == t.Id)) continue;
                    var owner = t.Requesters[0];
                    if (t.Assignees.Any(a => a.Id == owner.Id) || t.Responsible?.Id == owner.Id) continue;
                    db.TicketSurveyInvitations.Add(new() { Id = Guid.NewGuid().ToString("N"), Source = Source, TicketId = t.Id, RequesterId = owner.Id,
                        Owner = owner.Login, ResponsibleId = t.Responsible?.Id ?? 0, ResponsibleName = t.Responsible?.Name ?? "Команда поддержки", ClosedAt = t.ClosedAt!.Value,
                        Mandatory = policy.Applies(t), ExpiresAt = t.ClosedAt.Value.AddDays(14), NextNoticeAt = now });
                }
            await db.SaveChangesAsync(ct);
        }
        finally { gate.Surveys.Release(); }
    }
    public static bool FullAccess(PanelUser user) => Access.Can(user, "reports.glpi.all");
    public async Task<ReportTicket[]> Visible(PanelUser user, GlpiReportState state, CancellationToken ct)
    {
        var rows = JsonSerializer.Deserialize<ReportTicket[]>(state.Payload) ?? [];
        if (FullAccess(user)) return rows;
        if (user.AuthSource != "AD") return [];
        string owner = Security.NormalizeAccount(user.Username);
        var ids = user.BranchIds.Length > 0 ? user.BranchIds : user.BranchId.HasValue ? [user.BranchId.Value] : [];
        await Task.CompletedTask;
        return rows.Where(t => ids.Length > 0 ? t.BranchId.HasValue && ids.Contains(t.BranchId.Value) :
            t.Assignees.Any(a => Security.NormalizeAccount(a.Login) == owner) || t.Responsible != null && Security.NormalizeAccount(t.Responsible.Login) == owner).ToArray();
    }
    public async Task<object> Report(PanelUser user, ReportFilter filter, CancellationToken ct, int[]? entities = null)
    {
        var state = await State(ct);
        if (filter.Snapshot != null && filter.Snapshot != state.SyncedAt?.ToString("O")) throw new ArgumentException("Данные обновились. Обновите отчёт перед выгрузкой.");
        var visible = await Visible(user, state, ct);
        if (entities != null) visible = visible.Where(t => entities.Contains(t.EntityId)).ToArray();
        var reactionScope = visible.Where((filter with { Reaction = null }).Matches).ToArray();
        var rows = reactionScope.Where(filter.Matches).OrderByDescending(t => filter.Date(t)).ToArray();
        var surveyReport = SurveyReportData.Build(await db.TicketSurveyInvitations.AsNoTracking().Where(i=>i.Source==Source).ToListAsync(ct),rows,DateTime.UtcNow);
        return new { filter, syncedAt = state.SyncedAt, snapshot = state.SyncedAt?.ToString("O"), error = health.Error,
            surveysEnabled = state.SurveysEnabled, surveysFrom = state.SurveysFrom, totalAvailable = visible.Count(t => !t.Deleted), deleted = visible.Count(t => t.Deleted),
            coverageFrom = visible.Where(t => !t.Deleted).Select(t => t.CreatedAt).Min(), currentOpen = visible.Count(t => !t.Deleted && t.Status < 5),
            surveyReport, summary = ReportMath.Summary(rows), technicians = ReportMath.People(rows, true), requesters = ReportMath.People(rows, false), tickets = rows,
            reaction = GlpiReportReaction.Distribution(reactionScope), reactionMissing = reactionScope.Count(t => GlpiReportReaction.Bucket(t.AcceptanceSeconds) == null),
            choices = new { branches = visible.Where(t => t.BranchId != null).DistinctBy(t => t.BranchId).Select(t => new { id = t.BranchId, name = t.Branch }),
                technicians = visible.SelectMany(t => t.Assignees).Concat(visible.Where(t => t.Responsible != null).Select(t => t.Responsible!)).Where(ReportMath.IsTechnician).DistinctBy(a => a.Id), requesters = visible.SelectMany(t => t.Requesters).DistinctBy(a => a.Id) },
            timeline = visible.Where(t => !t.Deleted && (filter.Reaction == null || GlpiReportReaction.Bucket(t.AcceptanceSeconds) == filter.Reaction) && (filter.BranchId == null || t.BranchId == filter.BranchId) && (filter.Technician == null || t.Assignees.Any(a => a.Id == filter.Technician) || t.Responsible?.Id == filter.Technician) && (filter.Requester == null || t.Requesters.Any(a => a.Id == filter.Requester)) && (filter.Status == null || t.Status == filter.Status) && (filter.Search.Length == 0 || t.Title.Contains(filter.Search, StringComparison.OrdinalIgnoreCase) || t.Id.ToString().Contains(filter.Search))).SelectMany(t => new[] { (Kind: "created", At: t.CreatedAt), (Kind: "solved", At: t.SolvedAt), (Kind: "closed", At: t.ClosedAt) }).Where(e => e.At >= filter.Start && e.At < filter.End)
                .GroupBy(e => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(e.At!.Value, ReportMath.Zone))).OrderBy(g => g.Key).Select(g => new { day = g.Key, created = g.Count(e => e.Kind == "created"), solved = g.Count(e => e.Kind == "solved"), closed = g.Count(e => e.Kind == "closed") }) };
    }
    public static async Task EnsureSchema(HelperDb db)
    {
        // Additive and repeatable on both supported database providers.
        string time = db.Database.IsNpgsql() ? "timestamp with time zone" : "TEXT";
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS \"GlpiReportStates\" (\"Id\" TEXT PRIMARY KEY, \"Payload\" TEXT NOT NULL DEFAULT '[]', \"SyncedAt\" " + time + " NULL, \"SurveysFrom\" " + time + " NULL, \"SurveysEnabled\" boolean NOT NULL DEFAULT FALSE)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS \"TicketSurveyInvitations\" (\"Id\" TEXT PRIMARY KEY, \"Source\" TEXT NOT NULL, \"TicketId\" INTEGER NOT NULL, \"RequesterId\" INTEGER NOT NULL, \"Owner\" TEXT NOT NULL, \"ResponsibleId\" INTEGER NOT NULL, \"ResponsibleName\" TEXT NOT NULL, \"ClosedAt\" " + time + " NOT NULL, \"ExpiresAt\" " + time + " NOT NULL, \"NextNoticeAt\" " + time + " NOT NULL, \"AnsweredAt\" " + time + " NULL, \"State\" TEXT NOT NULL, \"Notices\" INTEGER NOT NULL DEFAULT 0)");
        await ChatBroadcasts.Column(db, "TicketSurveyInvitations", "AnswerRequestId", "TEXT NOT NULL DEFAULT ''");
        await ChatBroadcasts.Column(db, "TicketSurveyInvitations", "AnswerHash", "TEXT NOT NULL DEFAULT ''");
        await ChatBroadcasts.Column(db, "GlpiReportStates", "SurveyPolicyJson", "TEXT NOT NULL DEFAULT '{{}}'");
        await ChatBroadcasts.Column(db, "TicketSurveyInvitations", "LastSentAt", time + " NULL");
        await ChatBroadcasts.Column(db, "TicketSurveyInvitations", "Mandatory", "boolean NOT NULL DEFAULT FALSE");
        await ChatBroadcasts.Column(db, "TicketSurveyInvitations", "FirstShownAt", time + " NULL");
        await ChatBroadcasts.Column(db, "TicketSurveyInvitations", "LastShownAt", time + " NULL");
        await ChatBroadcasts.Column(db, "TicketSurveyInvitations", "DueAt", time + " NULL");
        await ChatBroadcasts.Column(db, "TicketSurveyInvitations", "DeferralDay", "TEXT NOT NULL DEFAULT ''");
        await ChatBroadcasts.Column(db, "TicketSurveyInvitations", "Deferrals", "INTEGER NOT NULL DEFAULT 0");
        await ChatBroadcasts.Column(db, "TicketSurveyInvitations", "LeaseUntil", time + " NULL");
        await ChatBroadcasts.Column(db, "TicketSurveyInvitations", "LeaseOwner", "TEXT NOT NULL DEFAULT ''");
        await ChatBroadcasts.Column(db, "TicketSurveyInvitations", "PendingScore", "INTEGER NOT NULL DEFAULT 0");
        await ChatBroadcasts.Column(db, "TicketSurveyInvitations", "PendingComment", "TEXT NOT NULL DEFAULT ''");
        await ChatBroadcasts.Column(db, "TicketSurveyInvitations", "PendingRequestId", "TEXT NOT NULL DEFAULT ''");
        await ChatBroadcasts.Column(db, "TicketSurveyInvitations", "NextWriteAt", time + " NULL");
        await ChatBroadcasts.Column(db, "TicketSurveyInvitations", "WriteAttempts", "INTEGER NOT NULL DEFAULT 0");
        await ChatBroadcasts.Column(db, "TicketSurveyInvitations", "LastError", "TEXT NOT NULL DEFAULT ''");
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_TicketSurveys_SourceTicket\" ON \"TicketSurveyInvitations\" (\"Source\", \"TicketId\")");
    }
}
public sealed class GlpiReportWorker(IServiceScopeFactory scopes, ReportHealth health, ILogger<GlpiReportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), ct);
        while (!ct.IsCancellationRequested)
        {
            try {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<GlpiReports>().Sync(ct);
                await scope.ServiceProvider.GetRequiredService<TicketSurveys>().Notify(ct);
                try { await scope.ServiceProvider.GetRequiredService<SurveyAlerts>().Notify(ct); }
                catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
                catch(Exception e) { logger.LogWarning("GLPI survey administrator notifications failed: {Type}",e.GetType().Name); }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception e) { health.Error = "Не удалось обновить данные GLPI. Показан последний сохранённый срез."; logger.LogWarning("GLPI reports sync failed: {Type}", e.GetType().Name); }
            await Task.Delay(TimeSpan.FromMinutes(2), ct);
        }
    }
}
