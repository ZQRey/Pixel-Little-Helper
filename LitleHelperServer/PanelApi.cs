using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;

namespace LitleHelperServer;

public static class PanelApi
{
    public static void MapPanelApi(this WebApplication app)
    {
        app.MapPost("/api/auth/login", async (LoginRequest request, HelperDb db, IConfiguration config) =>
        {
            if (request.Username.Length > 100 || request.Password.Length > 72) return Results.Unauthorized();
            string login = Security.Login(request.Username);
            var user = await db.Users.SingleOrDefaultAsync(u => u.Username == login);
            if (user == null || !user.IsActive || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash)) return Results.Unauthorized();
            return Results.Ok(new { token = Security.Token(user, config), user });
        }).RequireRateLimiting("login");
        app.MapGet("/api/auth/me", async (ClaimsPrincipal p, HelperDb db) => Results.Ok(await db.Users.SingleAsync(u => u.Username == p.Identity!.Name))).RequireAuthorization("Panel");
        app.MapPost("/api/auth/change-password", async (PasswordRequest request, ClaimsPrincipal p, HelperDb db, IConfiguration config, PanelSessions sessions) =>
        {
            var user = await db.Users.SingleAsync(u => u.Username == p.Identity!.Name);
            if (!Security.PasswordValid(request.NewPassword) || request.NewPassword == request.CurrentPassword || !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
                return Results.BadRequest(new { error = "Проверьте текущий пароль. Новый пароль: 8–72 символа, до 72 байт UTF-8." });
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, 12); user.MustChangePassword = false; user.SecurityVersion++;
            await db.SaveChangesAsync(); sessions.Revoke(user.Id); return Results.Ok(new { token = Security.Token(user, config), user });
        }).RequireAuthorization("Panel");

        app.MapGet("/api/computers", async (ClaimsPrincipal p, HelperDb db) =>
        {
            var computers = await db.Computers.AsNoTracking().OrderBy(c => c.MachineName).ToListAsync();
            // Hardware/software and transport identifiers never appear in the status table.
            return Results.Ok(computers.Select(HelperHub.Status));
        }).RequireAuthorization("Staff");
        app.MapPost("/api/computers/enroll", async (EnrollmentRequest request, HelperDb db) =>
        {
            string name = Security.Canonical(request.MachineName);
            if (!Security.MachineValid(name)) return Results.BadRequest(new { error = "Неверное имя ПК" });
            var computer = await db.Computers.SingleOrDefaultAsync(c => c.MachineName == name);
            if (computer?.IsOnline == true) return Results.Conflict(new { error = "Сначала отключите помощника для смены ключа" });
            if (computer == null) { computer = new Computer { MachineName = name }; db.Computers.Add(computer); }
            string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)); computer.AgentKeyHash = Security.KeyHash(key);
            await db.SaveChangesAsync(); return Results.Ok(new { machineName = name, clientToken = key });
        }).RequireAuthorization("Manage");
        app.MapGet("/api/computers/{id:int}/inventory", async (int id, HelperDb db) =>
        {
            var c = await db.Computers.FindAsync(id);
            return c == null ? Results.NotFound() : Results.Ok(new { c.MachineName, c.IpAddress, c.OsVersion, c.LastSeen,
                hardware = JsonSerializer.Deserialize<JsonElement>(c.HardwareJson), software = JsonSerializer.Deserialize<JsonElement>(c.SoftwareJson) });
        }).RequireAuthorization("Super");
        app.MapDelete("/api/computers/{id:int}", async (int id, HelperDb db) =>
        {
            var c = await db.Computers.FindAsync(id); if (c == null) return Results.NotFound();
            if (c.IsOnline) return Results.Conflict(new { error = "Отключите ПК перед удалением регистрации" });
            db.Computers.Remove(c); await db.SaveChangesAsync(); return Results.NoContent();
        }).RequireAuthorization("Super");
        app.MapGet("/api/scripts", (IConfiguration config) => Results.Ok(config.GetSection("Scripts").GetChildren().Select(s => new { id = s.Key, title = s["Title"] ?? s.Key }))).RequireAuthorization("Manage");
        app.MapPost("/api/commands", async (CommandRequest request, ClaimsPrincipal p, CommandService service, CancellationToken token) => Results.Ok(await service.SendAsync(p, request, token))).RequireAuthorization("Manage");
        app.MapGet("/api/tasks/{taskId}", async (string taskId, ClaimsPrincipal p, HelperDb db) =>
        {
            var task = await db.AuditLogs.AsNoTracking().SingleOrDefaultAsync(t => t.TaskId == taskId);
            return task == null || (!p.IsInRole(Roles.SuperAdmin) && task.AdminUsername != p.Identity!.Name) ? Results.NotFound() : Results.Ok(task);
        }).RequireAuthorization("Manage");

        app.MapGet("/api/buttons", async (HelperDb db) => Results.Ok(await db.Buttons.AsNoTracking().OrderBy(b => b.OrderIndex).ThenBy(b => b.Id).ToListAsync())).RequireAuthorization("Manage");
        app.MapPost("/api/buttons", async (ActionButton button, ClaimsPrincipal p, HelperDb db) =>
        {
            ValidateButton(button, p); button.Id = 0; db.Buttons.Add(button); await db.SaveChangesAsync(); return Results.Ok(button);
        }).RequireAuthorization("Manage");
        app.MapPut("/api/buttons/{id:int}", async (int id, ActionButton button, ClaimsPrincipal p, HelperDb db) =>
        {
            ValidateButton(button, p); var old = await db.Buttons.FindAsync(id); if (old == null) return Results.NotFound();
            old.Title = button.Title; old.ActionType = button.ActionType; old.Payload = button.Payload; old.OrderIndex = button.OrderIndex;
            old.IsActive = button.IsActive; old.IconName = button.IconName; old.TargetGroup = button.TargetGroup;
            await db.SaveChangesAsync(); return Results.Ok(old);
        }).RequireAuthorization("Manage");
        app.MapDelete("/api/buttons/{id:int}", async (int id, HelperDb db) =>
        { var b = await db.Buttons.FindAsync(id); if (b == null) return Results.NotFound(); db.Remove(b); await db.SaveChangesAsync(); return Results.NoContent(); }).RequireAuthorization("Manage");
        app.MapPost("/api/buttons/apply", async (HelperDb db, IHubContext<HelperHub> hub) =>
        {
            var computers = await db.Computers.AsNoTracking().Where(c => c.IsOnline && c.ConnectionId != null).ToListAsync();
            foreach (var c in computers) await hub.Clients.Client(c.ConnectionId!).SendAsync("OnButtonsUpdated", await HelperHub.Buttons(db, c));
            return Results.Ok(new { notified = computers.Count });
        }).RequireAuthorization("Manage");

        app.MapGet("/api/users", async (HelperDb db) => Results.Ok(await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync())).RequireAuthorization("Super");
        app.MapPost("/api/users", async (UserRequest request, HelperDb db) =>
        {
            ValidateUser(request, true); string name = Security.Login(request.Username);
            if (await db.Users.AnyAsync(u => u.Username == name)) return Results.Conflict(new { error = "Логин уже существует" });
            var user = new PanelUser { Username = name, FullName = request.FullName.Trim(), Role = request.Role, IsActive = request.IsActive,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password!, 12), MustChangePassword = true };
            db.Users.Add(user); await db.SaveChangesAsync(); return Results.Ok(user);
        }).RequireAuthorization("Super");
        app.MapPut("/api/users/{id:int}", async (int id, UserRequest request, HelperDb db, PanelSessions sessions) =>
        {
            ValidateUser(request, false); var user = await db.Users.FindAsync(id); if (user == null) return Results.NotFound();
            if (user.Role == Roles.SuperAdmin && user.IsActive && (request.Role != Roles.SuperAdmin || !request.IsActive)) await ProtectLastSuper(db, user);
            string name = Security.Login(request.Username);
            if (await db.Users.AnyAsync(u => u.Id != id && u.Username == name)) return Results.Conflict(new { error = "Логин уже существует" });
            user.Username = name; user.FullName = request.FullName.Trim(); user.Role = request.Role; user.IsActive = request.IsActive; user.SecurityVersion++;
            if (!string.IsNullOrEmpty(request.Password)) { user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, 12); user.MustChangePassword = true; }
            await db.SaveChangesAsync(); sessions.Revoke(user.Id); return Results.Ok(user);
        }).RequireAuthorization("Super");
        app.MapDelete("/api/users/{id:int}", async (int id, HelperDb db, PanelSessions sessions) =>
        {
            var user = await db.Users.FindAsync(id); if (user == null) return Results.NotFound();
            await ProtectLastSuper(db, user); db.Users.Remove(user); await db.SaveChangesAsync(); sessions.Revoke(id); return Results.NoContent();
        }).RequireAuthorization("Super");
        app.MapGet("/api/audit", async (ClaimsPrincipal p, HelperDb db) =>
        {
            var query = db.AuditLogs.AsNoTracking();
            if (!p.IsInRole(Roles.SuperAdmin)) query = query.Where(l => l.AdminUsername == p.Identity!.Name && l.CommandType != "cmd" && l.CommandType != "powershell");
            return Results.Ok(await query.OrderByDescending(l => l.Timestamp).Take(500).ToListAsync());
        }).RequireAuthorization("Manage");
        app.MapDelete("/api/audit", async (HelperDb db) => { await db.AuditLogs.ExecuteDeleteAsync(); return Results.NoContent(); }).RequireAuthorization("Super");

        app.MapGet("/api/tickets", async (ClaimsPrincipal p, HelperDb db) =>
        {
            var query = db.Tickets.AsNoTracking();
            if (p.IsInRole(Roles.User)) query = query.Where(t => t.Username == p.Identity!.Name);
            return Results.Ok(await query.OrderByDescending(t => t.CreatedAt).Take(500).ToListAsync());
        }).RequireAuthorization("Panel");
        app.MapPost("/api/tickets", async (TicketRequest request, ClaimsPrincipal p, HelperDb db, GlpiService glpi, CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 8000 || request.Title.Length > 160) return Results.BadRequest();
            string user = p.Identity!.Name!; int id = await glpi.CreateTicketAsync(user, "WebPanel", request.Description, token);
            var ticket = new TicketRecord { GlpiId = id, Username = user, MachineName = "WebPanel", Title = request.Title, Description = request.Description };
            db.Tickets.Add(ticket); await db.SaveChangesAsync(token); return Results.Ok(ticket);
        }).RequireAuthorization("Staff");
        app.MapGet("/api/tickets/{id:int}", async (int id, ClaimsPrincipal p, HelperDb db, GlpiService glpi, CancellationToken token) =>
        {
            var ticket = await db.Tickets.FindAsync(id); if (ticket == null) return Results.NotFound();
            if (p.IsInRole(Roles.User) && ticket.Username != p.Identity!.Name) return Results.Forbid();
            return Results.Ok(new { local = ticket, glpi = await glpi.GetTicketAsync(ticket.GlpiId, token) });
        }).RequireAuthorization("Panel");
        app.MapPut("/api/tickets/{id:int}/status", async (int id, TicketStatus request, HelperDb db, GlpiService glpi, CancellationToken token) =>
        {
            var ticket = await db.Tickets.FindAsync(id); if (ticket == null) return Results.NotFound();
            await glpi.UpdateTicketAsync(ticket.GlpiId, request.Status, token); ticket.Status = request.Status.ToString();
            await db.SaveChangesAsync(token); return Results.Ok(ticket);
        }).RequireAuthorization("Staff");
    }
    private static async Task ProtectLastSuper(HelperDb db, PanelUser user)
    {
        if (user.IsActive && user.Role == Roles.SuperAdmin && await db.Users.CountAsync(u => u.IsActive && u.Role == Roles.SuperAdmin) <= 1)
            throw new ArgumentException("Нельзя удалить или отключить последнего SuperAdmin");
    }
    private static void ValidateUser(UserRequest request, bool requirePassword)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 100 || request.Username.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_' or '@' or '\\')) ||
            request.FullName.Length > 150 || !Roles.All.Contains(request.Role) || ((requirePassword || !string.IsNullOrEmpty(request.Password)) && !Security.PasswordValid(request.Password)))
            throw new ArgumentException("Проверьте логин, роль и пароль (8–72 символа)");
    }
    private static void ValidateButton(ActionButton b, ClaimsPrincipal p)
    {
        if (string.IsNullOrWhiteSpace(b.Title) || b.Title.Length > 100 || b.Payload.Length > 8000 || b.IconName.Length > 50 || b.TargetGroup.Length > 120 || b.OrderIndex is < 0 or > 10000 ||
            b.ActionType is not ("open_folder" or "open_url" or "run_command" or "ticket")) throw new ArgumentException("Неверные поля кнопки");
        if (b.TargetGroup != "All" && !b.TargetGroup.StartsWith("domain:", StringComparison.OrdinalIgnoreCase) && !b.TargetGroup.StartsWith("pc:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Группа: All, domain:DOMAIN или pc:PCNAME");
        if (b.ActionType == "run_command" && !p.IsInRole(Roles.SuperAdmin)) throw new UnauthorizedAccessException("Кнопки выполнения команд настраивает SuperAdmin");
        if (b.ActionType == "open_url" && (!Uri.TryCreate(b.Payload, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "search-ms")))
            throw new ArgumentException("Разрешены HTTP(S) и search-ms");
    }
    public record TicketStatus(int Status);
}
