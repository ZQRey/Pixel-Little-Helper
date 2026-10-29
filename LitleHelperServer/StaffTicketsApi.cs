using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace LitleHelperServer;

public record StaffTicketSummary(
    int Id,
    int GlpiId,
    string Title,
    string Description,
    string Username,
    string UserFullName,
    string MachineName,
    int? BranchId,
    string BranchName,
    string Room,
    string Status,
    string StatusName,
    int AssignedGlpiUserId,
    string AssignedUsername,
    DateTime CreatedAt,
    DateTime? SyncedAt,
    DateTime? ClosedAt,
    double? Score,
    string? ScoreComment
);

public record StaffTicketDetail(
    StaffTicketSummary Ticket,
    List<StaffTicketFollowup> Followups,
    List<StaffTicketSolution> Solutions,
    List<TicketAttachmentInfo> Attachments
);

public record StaffTicketFollowup(
    int Id,
    string Author,
    string Content,
    DateTime? Date,
    bool IsPrivate
);

public record StaffTicketSolution(
    int Id,
    string Author,
    string Content,
    DateTime? Date
);

public record StaffActionRequest(string Text);
public record StaffStatusRequest(int Status);

public static class StaffTicketsApi
{
    public static string StatusName(string status)
    {
        return TicketManagement.Status(status) switch
        {
            1 => "Новая",
            2 => "В работе (назначена)",
            3 => "В работе (запланирована)",
            4 => "В ожидании",
            5 => "Выполнена (решена)",
            6 => "Закрыта",
            _ => "Новая"
        };
    }

    public static void MapStaffTicketsApi(this WebApplication app)
    {
        app.MapGet("/api/staff-tickets/overview", async (ClaimsPrincipal p, HelperDb db, GlpiService glpi, CancellationToken token) =>
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Username == p.Identity!.Name, token);
            if (user == null || !user.IsActive || user.MustChangePassword || (user.Role != Roles.SuperAdmin && user.Role != Roles.Admin && !Access.Can(user, "tickets.manage")))
                return Results.Forbid();

            int glpiUserId = 0;
            string account = user.Username.Contains('@') ? user.Username.Split('@')[0] : user.Username;
            try { glpiUserId = await glpi.FindTechnicianAsync(account, token); }
            catch
            {
                if (user.Username != account)
                {
                    try { glpiUserId = await glpi.FindTechnicianAsync(user.Username, token); } catch { }
                }
            }

            string myUsername = user.Username.ToLowerInvariant();
            string myAccount = account.ToLowerInvariant();
            string myFullName = user.FullName.Trim();

            // Load scores & closed dates cache from GlpiReportStates
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

            var tickets = await db.Tickets.AsNoTracking()
                .OrderByDescending(t => t.CreatedAt)
                .Take(1500)
                .ToListAsync(token);

            bool IsMyTicket(TicketRecord t)
            {
                if (glpiUserId > 0 && t.AssignedGlpiUserId == glpiUserId) return true;
                if (!string.IsNullOrWhiteSpace(t.AssignedUsername))
                {
                    string au = t.AssignedUsername.Trim();
                    if (au.Equals(myUsername, StringComparison.OrdinalIgnoreCase) ||
                        au.Equals(myAccount, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(myFullName) && au.Equals(myFullName, StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
                return false;
            }

            bool IsUnassigned(TicketRecord t)
            {
                return (t.AssignedGlpiUserId == 0 || string.IsNullOrWhiteSpace(t.AssignedUsername));
            }

            StaffTicketSummary ToSummary(TicketRecord t)
            {
                double? score = null;
                DateTime? closedAt = null;
                if (scoreMap.TryGetValue(t.GlpiId, out var info))
                {
                    score = info.Score;
                    closedAt = info.ClosedAt;
                }
                return new StaffTicketSummary(
                    t.Id,
                    t.GlpiId,
                    t.Title,
                    t.Description,
                    t.Username,
                    t.UserFullName,
                    t.MachineName,
                    t.BranchId,
                    t.BranchName,
                    t.Room,
                    t.Status,
                    StatusName(t.Status),
                    t.AssignedGlpiUserId,
                    t.AssignedUsername,
                    t.CreatedAt,
                    t.SyncedAt,
                    closedAt,
                    score,
                    null
                );
            }

            var myActive = tickets.Where(t => TicketManagement.Status(t.Status) < 5 && IsMyTicket(t)).Select(ToSummary).ToList();
            var unassigned = tickets.Where(t => TicketManagement.Status(t.Status) < 5 && IsUnassigned(t)).Select(ToSummary).ToList();
            var myClosed = tickets.Where(t => TicketManagement.Status(t.Status) >= 5 && (IsMyTicket(t) || user.Role == Roles.SuperAdmin)).Select(ToSummary).ToList();

            return Results.Ok(new
            {
                technician = new { username = user.Username, fullName = user.FullName, role = user.Role, glpiUserId },
                myTickets = myActive,
                unassignedTickets = unassigned,
                closedTickets = myClosed
            });
        }).RequireAuthorization("Panel");

        app.MapGet("/api/staff-tickets/{id:int}", async (int id, ClaimsPrincipal p, HelperDb db, GlpiService glpi, CancellationToken token) =>
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Username == p.Identity!.Name, token);
            if (user == null || !user.IsActive || user.MustChangePassword || (user.Role != Roles.SuperAdmin && user.Role != Roles.Admin && !Access.Can(user, "tickets.manage")))
                return Results.Forbid();

            var ticket = await db.Tickets.FindAsync(new object[] { id }, token);
            if (ticket == null) return Results.NotFound();

            var followups = new List<StaffTicketFollowup>();
            try
            {
                var fJson = await glpi.FollowupsAsync(ticket.GlpiId, token);
                if (fJson.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in fJson.EnumerateArray())
                    {
                        int fid = GlpiService.Number(f.GetProperty("id"));
                        string author = GlpiService.Text(f, "users_id");
                        string content = GlpiTicketImport.Plain(GlpiService.Text(f, "content"));
                        DateTime? dt = null;
                        if (f.TryGetProperty("date_creation", out var dc) && DateTime.TryParse(dc.GetString(), out var d)) dt = d;
                        bool isPriv = f.TryGetProperty("is_private", out var ip) && GlpiService.Number(ip) != 0;
                        followups.Add(new(fid, author, content, dt, isPriv));
                    }
                }
            }
            catch { }

            var solutions = new List<StaffTicketSolution>();
            try
            {
                var sJson = await glpi.SolutionsAsync(ticket.GlpiId, token);
                if (sJson.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in sJson.EnumerateArray())
                    {
                        int sid = GlpiService.Number(s.GetProperty("id"));
                        string author = GlpiService.Text(s, "users_id");
                        string content = GlpiTicketImport.Plain(GlpiService.Text(s, "content"));
                        DateTime? dt = null;
                        if (s.TryGetProperty("date_creation", out var dc) && DateTime.TryParse(dc.GetString(), out var d)) dt = d;
                        solutions.Add(new(sid, author, content, dt));
                    }
                }
            }
            catch { }

            double? score = null;
            string? scoreComment = null;
            try
            {
                var satJson = await glpi.SatisfactionAsync(ticket.GlpiId, token);
                if (satJson.ValueKind == JsonValueKind.Array && satJson.GetArrayLength() > 0)
                {
                    var first = satJson[0];
                    if (first.TryGetProperty("satisfaction_scaled_to_5", out var sc) && double.TryParse(sc.ToString(), out double s)) score = s;
                    else if (first.TryGetProperty("satisfaction", out var sc2) && double.TryParse(sc2.ToString(), out double s2)) score = s2;
                    if (first.TryGetProperty("comment", out var cm)) scoreComment = cm.GetString();
                }
            }
            catch { }

            var attachments = await db.TicketAttachments.AsNoTracking()
                .Where(a => a.TicketId == id)
                .OrderBy(a => a.CreatedAt)
                .Select(a => new TicketAttachmentInfo(a.Id, a.TicketId, a.FileName, a.FileSize, a.CreatedAt))
                .ToListAsync(token);

            var summary = new StaffTicketSummary(
                ticket.Id,
                ticket.GlpiId,
                ticket.Title,
                ticket.Description,
                ticket.Username,
                ticket.UserFullName,
                ticket.MachineName,
                ticket.BranchId,
                ticket.BranchName,
                ticket.Room,
                ticket.Status,
                StatusName(ticket.Status),
                ticket.AssignedGlpiUserId,
                ticket.AssignedUsername,
                ticket.CreatedAt,
                ticket.SyncedAt,
                null,
                score,
                scoreComment
            );

            return Results.Ok(new StaffTicketDetail(summary, followups, solutions, attachments));
        }).RequireAuthorization("Panel");

        app.MapPost("/api/staff-tickets/{id:int}/claim", async (int id, ClaimsPrincipal p, HelperDb db, GlpiService glpi, CancellationToken token) =>
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Username == p.Identity!.Name, token);
            if (user == null || !user.IsActive || user.MustChangePassword || (user.Role != Roles.SuperAdmin && user.Role != Roles.Admin && !Access.Can(user, "tickets.manage")))
                return Results.Forbid();

            var ticket = await db.Tickets.FindAsync(new object[] { id }, token);
            if (ticket == null) return Results.NotFound();

            int glpiUserId = 0;
            string account = user.Username.Contains('@') ? user.Username.Split('@')[0] : user.Username;
            try { glpiUserId = await glpi.FindTechnicianAsync(account, token); }
            catch
            {
                if (user.Username != account)
                {
                    try { glpiUserId = await glpi.FindTechnicianAsync(user.Username, token); } catch { }
                }
            }

            if (glpiUserId > 0)
            {
                await glpi.AssignAsync(ticket.GlpiId, glpiUserId, token);
                ticket.AssignedGlpiUserId = glpiUserId;
            }
            ticket.AssignedUsername = !string.IsNullOrWhiteSpace(user.FullName) ? user.FullName : user.Username;
            ticket.Status = "2"; // In work (assigned)
            ticket.SyncedAt = DateTime.UtcNow;

            db.AuditLogs.Add(new()
            {
                AdminUsername = user.Username,
                MachineName = ticket.MachineName,
                CommandType = "staff_ticket_claim",
                CommandPayload = "GLPI #" + ticket.GlpiId,
                Status = "Completed",
                Result = "Принято в работу: " + ticket.AssignedUsername
            });
            await db.SaveChangesAsync(token);
            return Results.Ok(new { ok = true, assignedUsername = ticket.AssignedUsername, status = ticket.Status });
        }).RequireAuthorization("Panel");

        app.MapPost("/api/staff-tickets/{id:int}/reply", async (int id, StaffActionRequest request, ClaimsPrincipal p, HelperDb db, GlpiService glpi, IHubContext<HelperHub> hub, CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(request.Text)) return Results.BadRequest(new { error = "Текст ответа не может быть пустым" });
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Username == p.Identity!.Name, token);
            if (user == null || !user.IsActive || user.MustChangePassword || (user.Role != Roles.SuperAdmin && user.Role != Roles.Admin && !Access.Can(user, "tickets.manage")))
                return Results.Forbid();

            var ticket = await db.Tickets.FindAsync(new object[] { id }, token);
            if (ticket == null) return Results.NotFound();

            string authorName = !string.IsNullOrWhiteSpace(user.FullName) ? user.FullName : user.Username;
            await glpi.FollowupAsync(ticket.GlpiId, authorName, request.Text.Trim(), token);

            if (!string.IsNullOrWhiteSpace(ticket.Username))
            {
                var notice = new TicketReplyNotice(ticket.GlpiId, ticket.Title, authorName, request.Text.Trim());
                _ = hub.Clients.Group("AgentUser:" + ticket.Username.ToLowerInvariant()).SendAsync("TicketReplyNotice", notice, token);
            }

            db.AuditLogs.Add(new()
            {
                AdminUsername = user.Username,
                MachineName = ticket.MachineName,
                CommandType = "staff_ticket_reply",
                CommandPayload = "GLPI #" + ticket.GlpiId,
                Status = "Completed",
                Result = "Добавлен ответ"
            });
            await db.SaveChangesAsync(token);
            return Results.Ok(new { ok = true });
        }).RequireAuthorization("Panel");

        app.MapPost("/api/staff-tickets/{id:int}/solve", async (int id, StaffActionRequest request, ClaimsPrincipal p, HelperDb db, GlpiService glpi, IHubContext<HelperHub> hub, CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(request.Text)) return Results.BadRequest(new { error = "Текст решения не может быть пустым" });
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Username == p.Identity!.Name, token);
            if (user == null || !user.IsActive || user.MustChangePassword || (user.Role != Roles.SuperAdmin && user.Role != Roles.Admin && !Access.Can(user, "tickets.manage")))
                return Results.Forbid();

            var ticket = await db.Tickets.FindAsync(new object[] { id }, token);
            if (ticket == null) return Results.NotFound();

            string authorName = !string.IsNullOrWhiteSpace(user.FullName) ? user.FullName : user.Username;
            await glpi.SolveAsync(ticket.GlpiId, authorName, request.Text.Trim(), token);
            ticket.Status = "5"; // Solved
            ticket.SyncedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(ticket.Username))
            {
                var notice = new TicketReplyNotice(ticket.GlpiId, ticket.Title, authorName, "Решение заявки: " + request.Text.Trim());
                _ = hub.Clients.Group("AgentUser:" + ticket.Username.ToLowerInvariant()).SendAsync("TicketReplyNotice", notice, token);
            }

            db.AuditLogs.Add(new()
            {
                AdminUsername = user.Username,
                MachineName = ticket.MachineName,
                CommandType = "staff_ticket_solve",
                CommandPayload = "GLPI #" + ticket.GlpiId,
                Status = "Completed",
                Result = "Заявка решена"
            });
            await db.SaveChangesAsync(token);
            return Results.Ok(new { ok = true });
        }).RequireAuthorization("Panel");

        app.MapPost("/api/staff-tickets/{id:int}/status", async (int id, StaffStatusRequest request, ClaimsPrincipal p, HelperDb db, GlpiService glpi, CancellationToken token) =>
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Username == p.Identity!.Name, token);
            if (user == null || !user.IsActive || user.MustChangePassword || (user.Role != Roles.SuperAdmin && user.Role != Roles.Admin && !Access.Can(user, "tickets.manage")))
                return Results.Forbid();

            var ticket = await db.Tickets.FindAsync(new object[] { id }, token);
            if (ticket == null) return Results.NotFound();

            await glpi.UpdateTicketAsync(ticket.GlpiId, request.Status, token);
            ticket.Status = request.Status.ToString();
            ticket.SyncedAt = DateTime.UtcNow;

            db.AuditLogs.Add(new()
            {
                AdminUsername = user.Username,
                MachineName = ticket.MachineName,
                CommandType = "staff_ticket_status",
                CommandPayload = "GLPI #" + ticket.GlpiId,
                Status = "Completed",
                Result = "Статус " + request.Status
            });
            await db.SaveChangesAsync(token);
            return Results.Ok(new { ok = true, status = ticket.Status });
        }).RequireAuthorization("Panel");
    }
}
