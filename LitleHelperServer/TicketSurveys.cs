using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LitleHelperServer;

public record TicketSurveyNotice(string Id, int TicketId, string Title, string Specialist, DateTime ExpiresAt, bool Mandatory = false, DateTime? DueAt = null, DateTime? NextNoticeAt = null, int DeferralsLeft = 0, string State = "Pending", string Error = "", bool Required = false);
public record TicketSurveyAnswer(int Score, string? Comment, string RequestId);
public sealed class TicketSurveys(HelperDb db, GlpiReports reports, ReportGate gate, IConfiguration config, IHubContext<HelperHub> hub)
{
    private string KeyFile => config["GlpiReports:BridgeKeyFile"] ?? "data/glpi-rating-bridge.key";
    public bool Ready => File.Exists(KeyFile) && new FileInfo(KeyFile).Length >= 32;
    public async Task<object> Bridge(object body, CancellationToken ct)
    {
        if (!Ready) throw new InvalidOperationException("Мост оценки GLPI ещё не настроен");
        string json = JsonSerializer.Serialize(body), time = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string nonce = Guid.NewGuid().ToString("N");
        string signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(KeyFile).Trim()), Encoding.UTF8.GetBytes(time + "\n" + nonce + "\n" + json))).ToLowerInvariant();
        string source = reports.Source;
        var targetBuilder = new UriBuilder(source[..source.LastIndexOf("/apirest.php", StringComparison.OrdinalIgnoreCase)] + "/plugins/helperreports/Bridge") { Scheme = "https", Port = 443 };
        var target = new Uri(config["GlpiReports:BridgeUrl"] ?? targetBuilder.Uri.AbsoluteUri);
        if (target.Scheme != "https") throw new InvalidOperationException("Для записи оценки требуется HTTPS GLPI");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        string caFile = config["GlpiReports:BridgeCaFile"] ?? "data/glpi-bridge-ca.pem";
        if (File.Exists(caFile)) {
            handler.ServerCertificateCustomValidationCallback = (_, certificate, original, errors) => {
                if (certificate == null || (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0) return false;
                using var chain = new X509Chain(); chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.ImportFromPem(File.ReadAllText(caFile)); chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                if (original != null) foreach (var item in original.ChainElements.Cast<X509ChainElement>().Skip(1)) chain.ChainPolicy.ExtraStore.Add(item.Certificate);
                return chain.Build(certificate);
            };
        }
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        using var request = new HttpRequestMessage(HttpMethod.Post, target) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Helper-Time", time); request.Headers.Add("X-Helper-Nonce", nonce); request.Headers.Add("X-Helper-Signature", signature);
        using var response = await client.SendAsync(request, ct);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if ((int)response.StatusCode >= 500) throw new HttpRequestException("GLPI временно не сохранил оценку");
        if (!response.IsSuccessStatusCode) throw new ArgumentException(result.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "GLPI не сохранил оценку");
        return result.RootElement.Clone();
    }
    public async Task<PanelUser> RequireUser(ClaimsPrincipal principal, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Username == principal.Identity!.Name, ct);
        if (!user.IsActive || user.MustChangePassword || user.AuthSource != "AD") throw new UnauthorizedAccessException("Для оценки войдите под своей доменной учётной записью");
        return user;
    }
    private static bool Owner(TicketSurveyInvitation i, PanelUser u) => u.AuthSource == "AD" && Security.NormalizeAccount(i.Owner) == Security.NormalizeAccount(u.Username);
    private void Audit(string owner, string action, string detail) => db.AuditLogs.Add(new() { AdminUsername = owner, CommandType = "ticket_survey_" + action, CommandPayload = detail, Status = "Completed" });
    private static ReportTicket[] Rows(GlpiReportState state) => JsonSerializer.Deserialize<ReportTicket[]>(state.Payload) ?? [];
    private static TicketSurveyNotice Notice(TicketSurveyInvitation i, ReportTicket t, SurveyPolicy p)
    {
        var next=i.NextNoticeAt;
        if (p.Mandatory && i.Mandatory) {
            if (i.LastShownAt.HasValue) { var repeat=p.AfterBusinessMinutes(i.LastShownAt.Value,p.ReminderMinutes);if(repeat>next)next=repeat; }
            if (!p.Working(DateTime.UtcNow)) { var work=p.AfterBusinessMinutes(DateTime.UtcNow,1);if(work>next)next=work; }
        }
        return new(i.Id, i.TicketId, t.Title, i.ResponsibleName, i.ExpiresAt, p.Mandatory && i.Mandatory, i.DueAt,next,
            Math.Max(0, p.DailyDeferrals - (i.DeferralDay == SurveyPolicy.Day(DateTime.UtcNow).ToString("yyyy-MM-dd") ? i.Deferrals : 0)),
            i.State, i.LastError, p.Required(i, DateTime.UtcNow));
    }
    public async Task<TicketSurveyNotice[]> Pending(PanelUser user, CancellationToken ct, bool dueOnly = false)
    {
        var state = await reports.State(ct); if (!state.SurveysEnabled || user.AuthSource != "AD") return [];
        var p = SurveyPolicy.Read(state); var rows = Rows(state); var now = DateTime.UtcNow;
        var invites = await db.TicketSurveyInvitations.AsNoTracking().Where(i => i.Source == reports.Source).ToListAsync(ct);
        return invites.Where(i => Owner(i,user) && SurveyPolicy.Active(i) && i.ExpiresAt > now
            && (!dueOnly || i.State == "Pending" && i.NextNoticeAt <= now && (p.Mandatory && i.Mandatory || i.Notices < 3))
            && rows.Any(t => t.Id == i.TicketId && SurveyPolicy.Eligible(i,t)))
            .OrderBy(i => i.ClosedAt).Select(i => Notice(i,rows.Single(t => t.Id == i.TicketId),p)).ToArray();
    }
    private async Task<TicketSurveyInvitation> Owned(PanelUser user, string id, CancellationToken ct)
    {
        var i = await db.TicketSurveyInvitations.SingleOrDefaultAsync(i => i.Id == id && i.Source == reports.Source, ct);
        if (i == null || !Owner(i,user)) throw new UnauthorizedAccessException("Опрос недоступен");
        return i;
    }
    public async Task<bool> Claim(PanelUser user, string id, string device, CancellationToken ct)
    {
        await gate.Surveys.WaitAsync(ct);
        try {
            var i = await Owned(user,id,ct); var state = await reports.State(ct); var now = DateTime.UtcNow;
            if (!state.SurveysEnabled || i.State is not ("Pending" or "Blocked") || !Rows(state).Any(t => t.Id == i.TicketId && SurveyPolicy.Eligible(i,t))
                || i.ExpiresAt <= now || i.LeaseUntil > now && i.LeaseOwner != device) return false;
            bool requiredBefore = SurveyPolicy.Read(state).Required(i,now);
            i.LeaseOwner = device; i.LeaseUntil = now.AddMinutes(5); i.LastShownAt=now;
            if (i.FirstShownAt == null) { i.FirstShownAt = now; var due = SurveyPolicy.Read(state).AfterBusinessMinutes(now, SurveyPolicy.Read(state).DueBusinessHours * 60); i.DueAt = due < i.ExpiresAt ? due : i.ExpiresAt; }
            if(i.NextNoticeAt<=now)i.Notices++; if (!requiredBefore) i.NextNoticeAt = i.Mandatory && SurveyPolicy.Read(state).Mandatory ? SurveyPolicy.Read(state).AfterBusinessMinutes(now,SurveyPolicy.Read(state).ReminderMinutes) : now.AddDays(1);
            Audit(user.Username,"shown",i.TicketId.ToString()); await db.SaveChangesAsync(ct); return true;
        } finally { gate.Surveys.Release(); }
    }
    public async Task Acknowledge(PanelUser user, string id, CancellationToken ct) => await Claim(user,id,"legacy:"+user.Username,ct);
    public async Task Defer(PanelUser user, string id, bool dismiss, CancellationToken ct, int minutes = 30)
    {
        if (minutes is not (30 or 120)) throw new ArgumentException("Выберите отсрочку 30 минут или 2 часа");
        await gate.Surveys.WaitAsync(ct);
        try {
            var i = await Owned(user,id,ct); var state = await reports.State(ct); var p = SurveyPolicy.Read(state); var now = DateTime.UtcNow;
            if(i.ExpiresAt<=now)throw new ArgumentException("Истекли 14 дней после закрытия заявки");
            if (i.State != "Pending") throw new ArgumentException("Опрос уже изменился. Обновите список");
            if (p.Mandatory && i.Mandatory) {
                if (dismiss) throw new ArgumentException("Для этой заявки оценка обязательна. Доступна ограниченная отсрочка");
                var day = SurveyPolicy.Day(now).ToString("yyyy-MM-dd");
                if (i.DeferralDay != day) { i.DeferralDay = day; i.Deferrals = 0; }
                if (i.Deferrals >= p.DailyDeferrals || i.DueAt <= now) throw new ArgumentException("Срок отсрочки исчерпан. Оцените результат заявки");
                i.Deferrals++; i.NextNoticeAt = p.AfterBusinessMinutes(now,minutes);
            } else { i.NextNoticeAt = dismiss ? i.ExpiresAt : now.AddDays(1); if (dismiss) i.Notices = 3; }
            i.LeaseOwner = ""; i.LeaseUntil = null; i.LastShownAt=null;Audit(user.Username,"deferred",i.TicketId+":"+minutes); await db.SaveChangesAsync(ct);
            await Changed(i,ct);
        } finally { gate.Surveys.Release(); }
    }
    private Task Changed(TicketSurveyInvitation i, CancellationToken ct) => hub.Clients.Group("AgentUser:"+Security.NormalizeAccount(i.Owner)).SendAsync("TicketSurveyChanged",i.Id,ct);
    public async Task<object> Answer(PanelUser user, string id, TicketSurveyAnswer input, CancellationToken ct)
    {
        if (input.Score is < 1 or > 5 || input.Comment?.Length > 2000 || !Guid.TryParse(input.RequestId,out _)) throw new ArgumentException("Выберите от 1 до 5 звёзд; комментарий — до 2000 символов");
        await gate.Surveys.WaitAsync(ct);
        try {
            var i = await Owned(user,id,ct); var state = await reports.State(ct);
            if (!state.SurveysEnabled) throw new ArgumentException("Опросы приостановлены");
            if(i.State!="Answered" && i.ExpiresAt<=DateTime.UtcNow)throw new ArgumentException("Истекли 14 дней после закрытия заявки");
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { input.Score, Comment = input.Comment ?? "" }))));
            if (i.AnswerRequestId == input.RequestId || i.PendingRequestId == input.RequestId) {
                if (i.AnswerHash != hash) throw new ArgumentException("Ключ повтора использован для другого ответа");
                return new { saved = i.State == "Answered", queued = i.State == "Writing" };
            }
            if (i.State == "Writing") throw new ArgumentException("Предыдущая оценка ожидает сохранения в GLPI");
            if (i.State is not ("Pending" or "Blocked") || i.ExpiresAt <= DateTime.UtcNow
                || !Rows(state).Any(t => t.Id == i.TicketId && SurveyPolicy.Eligible(i,t))) throw new ArgumentException("Опрос уже завершён или заявка изменилась");
            i.State = "Writing"; i.PendingScore = input.Score; i.PendingComment = input.Comment ?? ""; i.PendingRequestId = input.RequestId;
            i.AnswerHash = hash; i.NextWriteAt = DateTime.UtcNow; i.WriteAttempts = 0; i.LastError = "";
            // Commit intent BEFORE talking to GLPI. Retry uses exactly the same answer and request ID.
            Audit(user.Username,"queued",i.TicketId.ToString()); await db.SaveChangesAsync(ct);
            await Write(i,ct); await Changed(i,ct);
            return new { saved = i.State == "Answered", queued = i.State == "Writing", error = i.LastError };
        } finally { gate.Surveys.Release(); }
    }
    private async Task Write(TicketSurveyInvitation i, CancellationToken ct)
    {
        try {
            var result = (JsonElement)await Bridge(new { operation = "answer", ticketId = i.TicketId, requesterId = i.RequesterId, username = i.Owner,
                closedAt = i.ClosedAt.ToString("yyyy-MM-dd HH:mm:ss"), score = i.PendingScore, comment = i.PendingComment, requestId = i.PendingRequestId, mandatory = i.Mandatory },ct);
            if (!result.TryGetProperty("saved",out var saved) || !saved.GetBoolean()) throw new HttpRequestException("GLPI не подтвердил запись");
            i.State = "Answered"; i.AnsweredAt = DateTime.UtcNow; i.AnswerRequestId = i.PendingRequestId; i.LastError = ""; i.PendingComment = "";
            Audit(i.Owner,"answered",i.TicketId.ToString());
        } catch (ArgumentException e) { i.State = "Blocked"; i.LastError = e.Message; Audit(i.Owner,"blocked",i.TicketId.ToString()); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException) {
            i.WriteAttempts++; i.NextWriteAt = DateTime.UtcNow.AddMinutes(Math.Min(60,Math.Pow(2,Math.Min(i.WriteAttempts,6))));
            i.LastError = "Оценка принята помощником и ожидает записи в GLPI";
        }
        await db.SaveChangesAsync(CancellationToken.None);
    }
    public async Task Retry(CancellationToken ct)
    {
        await gate.Surveys.WaitAsync(ct);
        try {
            var state = await reports.State(ct); if (!state.SurveysEnabled) return;
            var rows = Rows(state); var now = DateTime.UtcNow;
            foreach (var i in await db.TicketSurveyInvitations.Where(i => i.Source == reports.Source && i.State == "Writing" && i.NextWriteAt <= now).Take(20).ToListAsync(ct)) {
                if (!rows.Any(t => t.Id == i.TicketId && SurveyPolicy.Eligible(i,t))) continue;
                await Write(i,ct); await Changed(i,ct);
            }
        } finally { gate.Surveys.Release(); }
    }
    public async Task Exempt(PanelUser admin,string id,string reason,CancellationToken ct)
    {
        if (!Access.Can(admin,"reports.glpi.settings") || string.IsNullOrWhiteSpace(reason) || reason.Length>500) throw new UnauthorizedAccessException("Укажите причину исключения; требуется право управления опросами");
        await gate.Surveys.WaitAsync(ct);
        try {
            var i=await db.TicketSurveyInvitations.SingleOrDefaultAsync(i=>i.Id==id && i.Source==reports.Source,ct) ?? throw new ArgumentException("Опрос не найден");
            if (!(await reports.Visible(admin,await reports.State(ct),ct)).Any(t=>t.Id==i.TicketId)) throw new UnauthorizedAccessException("Заявка вне области доступа");
            if (i.State is not ("Pending" or "Blocked")) throw new ArgumentException("Этот опрос уже завершён или ожидает записи");
            i.State="Exempt";i.LastError=reason.Trim();i.PendingComment="";
            Audit(admin.Username,"exempt",i.TicketId+": "+reason.Trim());await db.SaveChangesAsync(ct);await Changed(i,ct);
        } finally { gate.Surveys.Release(); }
    }
    public async Task EnsureCanCreate(PanelUser user, bool urgent, CancellationToken ct, int? requesterId = null)
    {
        // Read fresh GLPI state before enforcing a restriction, including answers made in GLPI itself.
        var state = await reports.State(ct); if (!state.SurveysEnabled || !SurveyPolicy.Read(state).Mandatory) return;
        try { await reports.Sync(ct); } catch (Exception e) when (e is HttpRequestException or InvalidOperationException or TaskCanceledException) { return; }
        var pending = await Pending(user,ct);
        if (requesterId.HasValue) {
            var ownIds=await db.TicketSurveyInvitations.AsNoTracking().Where(i=>i.Source==reports.Source && i.RequesterId==requesterId).Select(i=>i.Id).ToListAsync(ct);
            pending=pending.Where(i=>ownIds.Contains(i.Id)).ToArray();
        }
        if (!pending.Any(i => i.Required)) return;
        if (urgent) { Audit(user.Username,"urgent_bypass","Срочная заявка при наличии обязательной оценки"); await db.SaveChangesAsync(ct); return; }
        throw new Microsoft.AspNetCore.SignalR.HubException("SURVEY_REQUIRED: Оцените выполненную заявку в меню «Оценить результат ★» либо выберите срочное обращение");
    }
    public async Task Notify(CancellationToken ct)
    {
        await Retry(ct);
        await gate.Surveys.WaitAsync(ct);
        try {
            var state = await reports.State(ct); if (!state.SurveysEnabled) return;
            var p = SurveyPolicy.Read(state); var now = DateTime.UtcNow; var rows = Rows(state);
            var invites = await db.TicketSurveyInvitations.Where(i => i.Source == reports.Source && i.State == "Pending" && i.NextNoticeAt <= now).ToListAsync(ct);
            var online = await db.Computers.Where(c => c.IsOnline && c.ConnectionId != null).ToListAsync(ct);
            foreach (var i in invites) {
                bool mandatory = p.Mandatory && i.Mandatory;
                if (i.LastSentAt.HasValue && now < (mandatory ? p.AfterBusinessMinutes(i.LastSentAt.Value,p.ReminderMinutes) : i.LastSentAt.Value.AddDays(1))) continue;
                if ((i.ExpiresAt <= now) || (!mandatory && i.Notices >= 3) || mandatory && !p.Working(now)) continue;
                var t = rows.SingleOrDefault(t => t.Id == i.TicketId && SurveyPolicy.Eligible(i,t)); if (t == null) continue;
                bool sent=false;
                foreach (var c in online.Where(c => Security.NormalizeAccount(c.CurrentUser) == Security.NormalizeAccount(i.Owner))) {
                    var verified = await AgentUserSessions.UserAsync(c,db); if (verified == null || !Owner(i,verified)) continue;
                    var client = hub.Clients.Client(c.ConnectionId!);
                    await client.SendAsync("TicketSurveyAvailable",Notice(i,t,p),ct);
                    if (i.Notices == 0 || i.NextNoticeAt <= now) await client.SendAsync("TicketReplyNotice", new TicketReplyNotice(i.TicketId,t.Title,"Поддержка","Заявка закрыта. Оцените результат от 1 до 5 звёзд в меню помощника «Оценить результат ★»."),ct);
                    sent=true;
                }
                // Sending is not proof of delivery. Only Claim/Acknowledge starts the answer deadline.
                if(sent) {
                    i.LastSentAt = now;
                    if(!mandatory) {i.Notices++;i.NextNoticeAt=now.AddDays(1);}
                }
            }
            await db.SaveChangesAsync(ct);
        } finally { gate.Surveys.Release(); }
    }
}

public static class GlpiReportEndpoints
{
    public static void MapGlpiReports(this WebApplication app)
    {
        app.MapGet("/api/reports/glpi", async (HttpRequest request, ClaimsPrincipal p, HelperDb db, GlpiReports reports, CancellationToken ct) =>
            Results.Ok(await reports.Report(await db.Users.AsNoTracking().SingleAsync(u => u.Username == p.Identity!.Name, ct), ReportFilter.Parse(request.Query), ct))).RequireAuthorization("reports.glpi.view");
        app.MapPost("/api/reports/glpi/sync", async (GlpiReports reports, CancellationToken ct) => { await reports.Sync(ct); return Results.Ok(new { ok = true }); }).RequireAuthorization("reports.glpi.settings").RequireRateLimiting("registration");
        app.MapGet("/api/reports/glpi/alerts", async (ClaimsPrincipal p,HelperDb db,SurveyAlerts alerts,CancellationToken ct) => Results.Ok(await alerts.Current(await db.Users.SingleAsync(u=>u.Username==p.Identity!.Name,ct),ct))).RequireAuthorization("reports.glpi.settings").RequireAuthorization("reports.glpi.view");
        app.MapPost("/api/reports/glpi/alerts/read", async (SurveyAlertRead input,ClaimsPrincipal p,HelperDb db,SurveyAlerts alerts,CancellationToken ct) => { await alerts.Read(await db.Users.SingleAsync(u=>u.Username==p.Identity!.Name,ct),input.Revision,ct);return Results.Ok(); }).RequireAuthorization("reports.glpi.settings").RequireAuthorization("reports.glpi.view");
        app.MapGet("/api/reports/glpi/settings", async (GlpiReports reports, TicketSurveys surveys,HelperDb db, CancellationToken ct) => {
            var recipients=(await db.Users.AsNoTracking().Where(u=>u.IsActive && !u.MustChangePassword).ToListAsync(ct)).Where(u=>SurveyAlerts.Authorized(u,new())).Select(u=>new {u.Id,u.Username,u.FullName}).ToArray();
            var state = await reports.State(ct); return Results.Ok(new { state.SurveysEnabled, state.SurveysFrom, policy = SurveyPolicy.Read(state), recipients, bridgeReady = surveys.Ready });
        }).RequireAuthorization("reports.glpi.settings");
        app.MapPost("/api/reports/glpi/settings", async (SurveySwitch change, ClaimsPrincipal p, HelperDb db, GlpiReports reports, TicketSurveys surveys, ReportGate gate, IConfiguration config, IHubContext<HelperHub> hub, CancellationToken ct) => {
            if (change.Enabled) await surveys.Bridge(new { operation = "probe" }, ct);
            await gate.Sync.WaitAsync(ct);
            try {
                var state = await reports.State(ct); state.SurveysEnabled = change.Enabled;
                if (change.Policy != null) {
                    change.Policy.Validate(); var old = SurveyPolicy.Read(state);
                    // Turning the mode on starts a fresh cohort; historical closures stay voluntary.
                    var policy = change.Policy with { From = change.Policy.Mandatory ? old.Mandatory ? old.From : DateTime.UtcNow : old.From };
                    state.SurveyPolicyJson = JsonSerializer.Serialize(policy);
                } if (change.Enabled) state.SurveysFrom ??= DateTime.UtcNow;
                var button = await db.Buttons.SingleOrDefaultAsync(b => b.SystemKey == "ticket-surveys", ct);
                if (button == null && change.Enabled) { button = new() { SystemKey = "ticket-surveys", IsSystem = true, Title = "Оценить результат ★", ActionType = "open_url", OrderIndex = 10001, IconName = "ticket" }; db.Buttons.Add(button); }
                if (button != null) { button.IsActive = change.Enabled; button.Payload = config["GlpiReports:PanelUrl"] ?? "https://helper.gp1.loc/?page=surveys"; }
                db.AuditLogs.Add(new() { AdminUsername = p.Identity!.Name!, CommandType = "glpi_surveys_settings", CommandPayload = change.Enabled ? "enabled" : "paused", Status = "Completed", Result = "Старые закрытия не опрашиваются" });
                await db.SaveChangesAsync(ct); await SystemButtons.NotifyAsync(db, hub); return Results.Ok(new { state.SurveysEnabled, state.SurveysFrom, policy = SurveyPolicy.Read(state) });
            } finally { gate.Sync.Release(); }
        }).RequireAuthorization("reports.glpi.settings");
        app.MapGet("/api/reports/glpi/export/{format}", async (string format, HttpRequest request, ClaimsPrincipal p, HelperDb db, GlpiReports reports, CancellationToken ct) => {
            if (format is not ("xlsx" or "pdf")) return Results.BadRequest(new { error = "Неизвестный формат" });
            var filter = ReportFilter.Parse(request.Query); var user = await db.Users.AsNoTracking().SingleAsync(u => u.Username == p.Identity!.Name, ct);
            var data = JsonSerializer.SerializeToElement(await reports.Report(user, filter, ct), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var rows = data.GetProperty("tickets").Deserialize<ReportTicket[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
            if (rows.Length > 10000) return Results.BadRequest(new { error = "Выгрузка больше 10000 заявок. Уменьшите период." });
            byte[] bytes = format == "xlsx" ? GlpiReportExport.Xlsx(data, rows, filter) : GlpiReportExport.Pdf(data, rows, filter);
            db.AuditLogs.Add(new() { AdminUsername = user.Username, CommandType = "glpi_report_export", CommandPayload = format + " " + filter.From + " " + filter.To, Status = "Completed", Result = rows.Length + " заявок" }); await db.SaveChangesAsync(ct);
            return Results.File(bytes, format == "xlsx" ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "application/pdf", $"GLPI-{filter.From:yyyyMMdd}-{filter.To:yyyyMMdd}.{format}");
        }).RequireAuthorization("reports.glpi.export").RequireAuthorization("reports.glpi.view").RequireRateLimiting("registration");
        app.MapGet("/api/ticket-surveys/pending", async (ClaimsPrincipal p, TicketSurveys surveys, CancellationToken ct) => Results.Ok(await surveys.Pending(await surveys.RequireUser(p, ct), ct))).RequireAuthorization("Panel");
        app.MapPost("/api/ticket-surveys/{id}/answer", async (string id, TicketSurveyAnswer answer, ClaimsPrincipal p, TicketSurveys surveys, CancellationToken ct) => { return Results.Ok(await surveys.Answer(await surveys.RequireUser(p, ct), id, answer, ct)); }).RequireAuthorization("Panel").RequireRateLimiting("registration");
        app.MapPost("/api/ticket-surveys/{id}/claim", async (string id, ClaimsPrincipal p, TicketSurveys surveys, CancellationToken ct) => Results.Ok(new { claimed = await surveys.Claim(await surveys.RequireUser(p,ct),id,"web:"+p.Identity!.Name,ct) })).RequireAuthorization("Panel");
        app.MapPost("/api/ticket-surveys/{id}/exempt", async (string id, SurveyExemption input, ClaimsPrincipal p, TicketSurveys surveys, HelperDb db, CancellationToken ct) => { await surveys.Exempt(await db.Users.SingleAsync(u=>u.Username==p.Identity!.Name,ct),id,input.Reason,ct);return Results.Ok(); }).RequireAuthorization("reports.glpi.settings");
        app.MapPost("/api/ticket-surveys/{id}/snooze", async (string id, SurveySnooze input, ClaimsPrincipal p, TicketSurveys surveys, CancellationToken ct) => { await surveys.Defer(await surveys.RequireUser(p,ct),id,false,ct,input.Minutes); return Results.Ok(); }).RequireAuthorization("Panel");
        app.MapPost("/api/ticket-surveys/{id}/defer", async (string id, ClaimsPrincipal p, TicketSurveys surveys, CancellationToken ct) => { await surveys.Defer(await surveys.RequireUser(p, ct), id, false, ct); return Results.Ok(); }).RequireAuthorization("Panel");
        app.MapPost("/api/ticket-surveys/{id}/dismiss", async (string id, ClaimsPrincipal p, TicketSurveys surveys, CancellationToken ct) => { await surveys.Defer(await surveys.RequireUser(p, ct), id, true, ct); return Results.Ok(); }).RequireAuthorization("Panel");
    }
    public record SurveySwitch(bool Enabled, SurveyPolicy? Policy = null);
}

public record SurveySnooze(int Minutes);
public record SurveyExemption(string Reason);
public record SurveyAlertRead(int Revision);
