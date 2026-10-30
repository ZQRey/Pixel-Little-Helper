using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LitleHelperServer;

public record UserTicketSummary(
    int Id,
    int GlpiId,
    string Title,
    string Description,
    string Status,
    string StatusName,
    string BranchName,
    string Room,
    string AssignedSpecialist,
    DateTime CreatedAt,
    DateTime? ClosedAt,
    double? Score,
    string? ScoreComment,
    bool CanRate,
    int FollowupsCount
);

public record UserTicketDetail(
    UserTicketSummary Ticket,
    List<StaffTicketFollowup> Followups,
    List<StaffTicketSolution> Solutions,
    List<TicketAttachmentInfo> Attachments
);

public record UserRateRequest(int Score, string? Comment);

public static class UserTicketsApi
{
    public static void MapUserTicketsApi(this WebApplication app)
    {
        // 1. Get user tickets list
        app.MapGet("/api/user-tickets", async (HttpRequest request, HelperDb db, GlpiService glpi, CancellationToken token) =>
        {
            var (c, owner, _) = await ResolveAgentCallerAsync(request, db, token);
            if (c == null) return Results.Unauthorized();

            var list = await GetUserTicketsCoreAsync(db, glpi, c, owner, token);
            return Results.Ok(list);
        });

        // 2. Get user ticket detail
        app.MapGet("/api/user-tickets/{id:int}", async (int id, HttpRequest request, HelperDb db, GlpiService glpi, CancellationToken token) =>
        {
            var (c, owner, _) = await ResolveAgentCallerAsync(request, db, token);
            if (c == null) return Results.Unauthorized();

            var detail = await GetUserTicketDetailCoreAsync(db, glpi, c, owner, id, token);
            return detail == null ? Results.NotFound() : Results.Ok(detail);
        });

        // 3. Rate user ticket
        app.MapPost("/api/user-tickets/{id:int}/rate", async (int id, UserRateRequest body, HttpRequest request, HelperDb db, GlpiService glpi, TicketSurveys surveys, CancellationToken token) =>
        {
            var (c, owner, _) = await ResolveAgentCallerAsync(request, db, token);
            if (c == null) return Results.Unauthorized();

            bool ok = await RateUserTicketCoreAsync(db, glpi, surveys, c, owner, id, body.Score, body.Comment, token);
            return ok ? Results.Ok(new { success = true }) : Results.BadRequest("Не удалось сохранить оценку или срок оценки истёк.");
        });
    }

    internal static async Task<(Computer? Computer, string Owner, string MachineName)> ResolveAgentCallerAsync(HttpRequest request, HelperDb db, CancellationToken token)
    {
        string key = request.Headers["X-Client-Key"].ToString();
        string machine = Security.Canonical(request.Headers["X-Machine-Name"].ToString());

        if (string.IsNullOrEmpty(key) || !Security.MachineValid(machine))
            return (null, "", "");

        var computer = await db.Computers.SingleOrDefaultAsync(x => x.MachineName == machine, token);
        if (computer == null || string.IsNullOrEmpty(computer.AgentKeyHash)) return (null, "", "");

        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(computer.AgentKeyHash),
            System.Text.Encoding.ASCII.GetBytes(Security.KeyHash(key))))
        {
            return (null, "", "");
        }

        string owner = Security.TicketUser(computer.CurrentUser);
        return (computer, owner, computer.MachineName);
    }

    internal static async Task<List<UserTicketSummary>> GetUserTicketsCoreAsync(HelperDb db, GlpiService glpi, Computer c, string owner, CancellationToken token)
    {
        string reportedUser = Security.TicketUser(c.CurrentUser).ToLowerInvariant();
        string machine = c.MachineName.ToLowerInvariant();
        string cleanOwner = owner.ToLowerInvariant();

        // 1. Fetch tickets matching this user or computer
        var tickets = await db.Tickets.AsNoTracking()
            .Where(t => t.Username.ToLower() == cleanOwner ||
                        t.Username.ToLower() == reportedUser ||
                        t.MachineName.ToLower() == machine)
            .OrderByDescending(t => t.CreatedAt)
            .Take(200)
            .ToListAsync(token);

        // 2. Fetch survey invitations / ratings map
        var glpiIds = tickets.Select(t => t.GlpiId).Where(id => id > 0).Distinct().ToList();
        var invitations = await db.TicketSurveyInvitations.AsNoTracking()
            .Where(i => glpiIds.Contains(i.TicketId))
            .ToListAsync(token);

        var invMap = invitations.ToDictionary(i => i.TicketId);

        // Also check report states cache for scores/closure
        Dictionary<int, (double? Score, DateTime? ClosedAt)> scoreMap = new();
        try
        {
            var rep = await db.GlpiReportStates.AsNoTracking().FirstOrDefaultAsync(token);
            if (rep != null && !string.IsNullOrWhiteSpace(rep.Payload))
            {
                var rows = JsonSerializer.Deserialize<ReportTicket[]>(rep.Payload);
                if (rows != null)
                {
                    foreach (var r in rows)
                    {
                        scoreMap[r.Id] = (r.CurrentScore ?? r.Score, r.ClosedAt);
                    }
                }
            }
        }
        catch { }

        var result = new List<UserTicketSummary>();
        var now = DateTime.UtcNow;

        var activeStaffUsers = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && (u.Role == Roles.SuperAdmin || u.Role == Roles.Admin))
            .ToListAsync(token);

        foreach (var t in tickets)
        {
            int stCode = StaffTicketsApi.StatusCode(t.Status);
            string stName = StaffTicketsApi.StatusName(t.Status);

            double? score = null;
            string? comment = null;
            DateTime? closedAt = null;
            bool isClosed = stCode >= 5;

            if (scoreMap.TryGetValue(t.GlpiId, out var sInfo))
            {
                score = sInfo.Score;
                closedAt = sInfo.ClosedAt;
            }

            if (invMap.TryGetValue(t.GlpiId, out var inv))
            {
                if (inv.State == "Answered" || inv.PendingScore > 0)
                {
                    score ??= inv.PendingScore > 0 ? inv.PendingScore : null;
                    comment ??= !string.IsNullOrWhiteSpace(inv.PendingComment) ? inv.PendingComment : null;
                }
                closedAt ??= inv.ClosedAt != default ? inv.ClosedAt : (DateTime?)null;
                isClosed = true;
            }

            // If closed but closedAt unknown, fall back to SyncedAt or CreatedAt
            if (isClosed && !closedAt.HasValue)
            {
                closedAt = t.SyncedAt ?? t.CreatedAt;
            }

            // 15 days rating window (exclude self-created or self-handled tickets by technicians/admins)
            bool canRate = false;
            if (isClosed && (!score.HasValue || score.Value <= 0))
            {
                DateTime referenceDate = closedAt ?? t.CreatedAt;
                if ((now - referenceDate).TotalDays <= 15.0 && !IsTechnicianSelfRating(t, cleanOwner, reportedUser, activeStaffUsers))
                {
                    canRate = true;
                }
            }

            string specialist = !string.IsNullOrWhiteSpace(t.AssignedUsername)
                ? t.AssignedUsername
                : "Назначается";

            result.Add(new UserTicketSummary(
                t.Id,
                t.GlpiId,
                t.Title,
                t.Description,
                t.Status,
                stName,
                t.BranchName,
                t.Room,
                specialist,
                t.CreatedAt,
                closedAt,
                score,
                comment,
                canRate,
                0
            ));
        }

        return result;
    }

    internal static async Task<UserTicketDetail?> GetUserTicketDetailCoreAsync(HelperDb db, GlpiService glpi, Computer c, string owner, int ticketId, CancellationToken token)
    {
        string reportedUser = Security.TicketUser(c.CurrentUser).ToLowerInvariant();
        string machine = c.MachineName.ToLowerInvariant();
        string cleanOwner = owner.ToLowerInvariant();

        // 1. Search strictly among tickets belonging to this user or computer
        var userScope = db.Tickets.AsNoTracking().Where(x =>
            x.Username.ToLower() == cleanOwner ||
            x.Username.ToLower() == reportedUser ||
            x.MachineName.ToLower() == machine);

        // Prefer exact DB Id match first, then GlpiId match
        var t = await userScope.FirstOrDefaultAsync(x => x.Id == ticketId, token)
             ?? await userScope.FirstOrDefaultAsync(x => x.GlpiId == ticketId, token);

        // 2. If not found in user's scope, allow Admin or SuperAdmin to view any ticket
        if (t == null)
        {
            var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(user =>
                user.IsActive && !user.MustChangePassword &&
                (user.Username.ToLower() == cleanOwner || user.Username.ToLower() == reportedUser || user.AssistantMachine == c.MachineName), token);
            if (u != null && (u.Role == Roles.Admin || u.Role == Roles.SuperAdmin || Access.Can(u, "tickets.manage")))
            {
                t = await db.Tickets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ticketId, token)
                 ?? await db.Tickets.AsNoTracking().FirstOrDefaultAsync(x => x.GlpiId == ticketId, token);
            }
        }

        if (t == null) return null;

        // Build summary directly for this ticket without re-querying the whole list
        int stCode = StaffTicketsApi.StatusCode(t.Status);
        string stName = StaffTicketsApi.StatusName(t.Status);
        double? score = null;
        string? comment = null;
        DateTime? closedAt = t.SyncedAt;
        bool isClosed = stCode >= 5;

        var inv = await db.TicketSurveyInvitations.AsNoTracking().FirstOrDefaultAsync(i => i.TicketId == t.GlpiId, token);
        if (inv != null)
        {
            if (inv.State == "Answered" || inv.PendingScore > 0)
            {
                score = inv.PendingScore > 0 ? inv.PendingScore : null;
                comment = !string.IsNullOrWhiteSpace(inv.PendingComment) ? inv.PendingComment : null;
            }
            if (inv.ClosedAt != default) closedAt = inv.ClosedAt;
            isClosed = true;
        }

        if (isClosed && !closedAt.HasValue) closedAt = t.SyncedAt ?? t.CreatedAt;

        var activeStaffUsers = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && (u.Role == Roles.SuperAdmin || u.Role == Roles.Admin))
            .ToListAsync(token);

        bool canRate = false;
        if (isClosed && (!score.HasValue || score.Value <= 0))
        {
            DateTime refDate = closedAt ?? t.CreatedAt;
            if ((DateTime.UtcNow - refDate).TotalDays <= 15.0 && !IsTechnicianSelfRating(t, cleanOwner, reportedUser, activeStaffUsers))
                canRate = true;
        }

        string specialist = !string.IsNullOrWhiteSpace(t.AssignedUsername) ? t.AssignedUsername : "Назначается";

        var summary = new UserTicketSummary(
            t.Id,
            t.GlpiId,
            t.Title,
            t.Description,
            t.Status,
            stName,
            t.BranchName,
            t.Room,
            specialist,
            t.CreatedAt,
            closedAt,
            score,
            comment,
            canRate,
            0
        );

        // Fetch followups from GLPI
        List<StaffTicketFollowup> followups = [];
        try
        {
            var followDoc = await glpi.FollowupsAsync(t.GlpiId, token);
            if (followDoc.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in followDoc.EnumerateArray())
                {
                    int fid = el.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
                    string content = el.TryGetProperty("content", out var cEl) ? cEl.GetString() ?? "" : "";
                    content = CleanHtml(content);

                    string author = "Специалист";
                    if (el.TryGetProperty("users_id", out var uidEl))
                    {
                        author = "Сотрудник #" + uidEl.GetInt32();
                    }

                    DateTime? dt = null;
                    if (el.TryGetProperty("date_creation", out var dEl) && DateTime.TryParse(dEl.GetString(), out var parsedDate))
                    {
                        dt = parsedDate;
                    }

                    bool isPriv = el.TryGetProperty("is_private", out var pEl) && (pEl.GetInt32() != 0 || pEl.GetString() == "1");
                    if (!isPriv) // Only public followups visible to user
                    {
                        followups.Add(new StaffTicketFollowup(fid, author, content, dt, false));
                    }
                }
            }
        }
        catch { }

        // Fetch solution from GLPI
        List<StaffTicketSolution> solutions = [];
        try
        {
            var solDoc = await glpi.SolutionsAsync(t.GlpiId, token);
            if (solDoc.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in solDoc.EnumerateArray())
                {
                    int sid = el.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
                    string content = el.TryGetProperty("content", out var cEl) ? cEl.GetString() ?? "" : "";
                    content = CleanHtml(content);

                    DateTime? dt = null;
                    if (el.TryGetProperty("date_creation", out var dEl) && DateTime.TryParse(dEl.GetString(), out var parsedDate))
                    {
                        dt = parsedDate;
                    }

                    solutions.Add(new StaffTicketSolution(sid, "Решение техподдержки", content, dt));
                }
            }
        }
        catch { }

        // Fetch attachments
        List<TicketAttachmentInfo> attachments = [];
        try
        {
            attachments = await db.TicketAttachments.AsNoTracking()
                .Where(a => a.TicketId == t.Id)
                .OrderBy(a => a.CreatedAt)
                .Select(a => new TicketAttachmentInfo(a.Id, a.TicketId, a.FileName, a.FileSize, a.CreatedAt))
                .ToListAsync(token);
        }
        catch { }

        return new UserTicketDetail(summary with { FollowupsCount = followups.Count }, followups, solutions, attachments);
    }

    internal static async Task<bool> RateUserTicketCoreAsync(HelperDb db, GlpiService glpi, TicketSurveys surveys, Computer c, string owner, int ticketId, int score, string? comment, CancellationToken token)
    {
        if (score < 1 || score > 5) return false;

        string reportedUser = Security.TicketUser(c.CurrentUser).ToLowerInvariant();
        string machine = c.MachineName.ToLowerInvariant();
        string cleanOwner = owner.ToLowerInvariant();

        var userScope = db.Tickets.Where(x =>
            x.Username.ToLower() == cleanOwner ||
            x.Username.ToLower() == reportedUser ||
            x.MachineName.ToLower() == machine);

        var t = await userScope.FirstOrDefaultAsync(x => x.Id == ticketId, token)
             ?? await userScope.FirstOrDefaultAsync(x => x.GlpiId == ticketId, token);

        if (t == null) return false;

        // Disallow technician self-rating (when technician rated their own ticket or is the assigned specialist)
        var activeStaffUsers = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && (u.Role == Roles.SuperAdmin || u.Role == Roles.Admin))
            .ToListAsync(token);

        if (IsTechnicianSelfRating(t, cleanOwner, reportedUser, activeStaffUsers))
            return false;

        // Verify <= 15 days
        DateTime referenceDate = t.SyncedAt ?? t.CreatedAt;
        var inv = await db.TicketSurveyInvitations.FirstOrDefaultAsync(i => i.TicketId == t.GlpiId, token);
        if (inv != null && inv.ClosedAt != default) referenceDate = inv.ClosedAt;

        if ((DateTime.UtcNow - referenceDate).TotalDays > 15.0)
            return false;

        // If survey invitation exists, update it
        if (inv != null)
        {
            inv.State = "Answered";
            inv.AnsweredAt = DateTime.UtcNow;
            inv.PendingScore = score;
            inv.PendingComment = comment ?? "";
            inv.LastError = "";
        }
        else
        {
            inv = new TicketSurveyInvitation
            {
                Id = Guid.NewGuid().ToString("N"),
                Source = "PixelHelper",
                TicketId = t.GlpiId,
                Owner = owner,
                ResponsibleName = t.AssignedUsername,
                ClosedAt = referenceDate,
                ExpiresAt = referenceDate.AddDays(15),
                State = "Answered",
                AnsweredAt = DateTime.UtcNow,
                PendingScore = score,
                PendingComment = comment ?? ""
            };
            db.TicketSurveyInvitations.Add(inv);
        }

        await db.SaveChangesAsync(token);

        // Try submitting satisfaction to GLPI
        try
        {
            if (surveys.Ready)
            {
                await surveys.Bridge(new
                {
                    operation = "answer",
                    ticketId = t.GlpiId,
                    requesterId = inv.RequesterId,
                    username = owner,
                    closedAt = referenceDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    score,
                    comment = comment ?? "",
                    requestId = Guid.NewGuid().ToString("N"),
                    mandatory = false
                }, token);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UserTicketsApi] Не удалось передать оценку в Bridge GLPI: {ex.Message}");
        }

        return true;
    }

    internal static bool IsTechnicianSelfRating(TicketRecord t, string cleanOwner, string reportedUser, IEnumerable<PanelUser> users)
    {
        string creator = (t.Username ?? "").Trim().ToLowerInvariant();
        string assigned = (t.AssignedUsername ?? "").Trim().ToLowerInvariant();
        string owner = (cleanOwner ?? "").Trim().ToLowerInvariant();
        string rep = (reportedUser ?? "").Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(assigned)) return false;

        static string StripDomain(string s) => s.Contains('\\') ? s.Substring(s.LastIndexOf('\\') + 1) : (s.Contains('@') ? s.Substring(0, s.IndexOf('@')) : s);
        string creatorShort = StripDomain(creator);
        string assignedShort = StripDomain(assigned);
        string ownerShort = StripDomain(owner);
        string repShort = StripDomain(rep);

        // 1. Direct or short name equality
        if (assigned == creator || assignedShort == creatorShort)
            return true;
        if (assigned == owner || assignedShort == ownerShort)
            return true;
        if (assigned == rep || assignedShort == repShort)
            return true;

        // 2. Lookup technician in PanelUser list
        foreach (var u in users)
        {
            string uName = (u.Username ?? "").Trim().ToLowerInvariant();
            string uNameShort = StripDomain(uName);
            string uFullName = (u.FullName ?? "").Trim().ToLowerInvariant();
            bool isTech = (assigned == uName || assignedShort == uNameShort || assigned == uFullName);
            if (isTech)
            {
                // Is the creator also this technician?
                if (creator == uName || creatorShort == uNameShort || creator == uFullName)
                    return true;

                // Is the current viewer/rater this technician?
                if (owner == uName || ownerShort == uNameShort || owner == uFullName ||
                    rep == uName || repShort == uNameShort || rep == uFullName)
                    return true;
            }
        }

        return false;
    }

    private static string CleanHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        string text = System.Text.RegularExpressions.Regex.Replace(html, "<br\\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, "</p>", "\n\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", string.Empty);
        return System.Net.WebUtility.HtmlDecode(text).Trim();
    }
}
