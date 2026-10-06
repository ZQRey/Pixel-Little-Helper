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
        app.MapPost("/api/agents/register", async (AgentRegistrationRequest request, HelperDb db) =>
        {
            if (string.IsNullOrWhiteSpace(request.MachineName) || string.IsNullOrWhiteSpace(request.ClientKey)) return Results.BadRequest();
            string machine = Security.Canonical(request.MachineName);
            if (!Security.MachineValid(machine) || request.ClientKey.Length is < 32 or > 200 ||
                request.ClientKey.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('+' or '/' or '=' or '-' or '_')))
                return Results.BadRequest(new { error = "Неверные данные регистрации клиента." });
            string hash = Security.KeyHash(request.ClientKey);
            var computer = await db.Computers.SingleOrDefaultAsync(c => c.MachineName == machine);
            if (computer == null)
            {
                computer = new Computer { MachineName = machine, AgentKeyHash = hash };
                db.Computers.Add(computer);
                try { await db.SaveChangesAsync(); }
                catch (DbUpdateException)
                {
                    db.Entry(computer).State = EntityState.Detached;
                    computer = await db.Computers.SingleOrDefaultAsync(c => c.MachineName == machine);
                    if (computer == null) throw;
                }
            }
            if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(computer.AgentKeyHash), System.Text.Encoding.ASCII.GetBytes(hash)))
                return Results.Conflict(new { error = "Компьютер уже зарегистрирован с другим ключом. Обратитесь к администратору." });
            return Results.Ok(new { registered = true });
        }).RequireRateLimiting("registration");
        app.MapGet("/api/settings/glpi", (GlpiSettingsStore settings) => Results.Ok(settings.View())).RequireAuthorization("settings.manage");
        app.MapPut("/api/settings/glpi", (GlpiSettingsUpdate update, GlpiSettingsStore settings) => { settings.Save(update); return Results.Ok(settings.View()); }).RequireAuthorization("settings.manage");
        app.MapPost("/api/settings/glpi/test", async (GlpiService glpi, CancellationToken token) =>
        {
            try { await glpi.TestConnectionAsync(token); return Results.Ok(new { success = true, message = "GLPI API: авторизация успешна. Тестовая заявка не создавалась." }); }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
            { return Results.Ok(new { success = false, message = ex is TaskCanceledException ? "GLPI не ответил за 20 секунд. Проверьте сеть и адрес." : ex is HttpRequestException ? "Сервер помощника не смог подключиться к GLPI. Проверьте адрес, DNS, TLS и сеть." : ex.Message }); }
        }).RequireAuthorization("settings.manage");
        app.MapPost("/api/auth/login", async (LoginRequest request, HelperDb db, IConfiguration config) =>
        {
            if (request.Username.Length > 100 || request.Password.Length > 72) return Results.Unauthorized();
            string login = Security.Login(request.Username);
            var user = await db.Users.SingleOrDefaultAsync(u => u.Username == login);
            if (user == null || !user.IsActive || user.AuthSource == "AD" || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash)) return Results.Unauthorized();
            return Results.Ok(new { token = Security.Token(user, config), user });
        }).RequireRateLimiting("login");
        app.MapGet("/api/auth/me", async (ClaimsPrincipal p, HelperDb db) => Results.Ok(await db.Users.SingleAsync(u => u.Username == p.Identity!.Name))).RequireAuthorization("Panel");
        app.MapPost("/api/auth/change-password", async (PasswordRequest request, ClaimsPrincipal p, HelperDb db, IConfiguration config, PanelSessions sessions) =>
        {
            var user = await db.Users.SingleAsync(u => u.Username == p.Identity!.Name);
            if (user.AuthSource == "AD") return Results.BadRequest(new { error = "Пароль AD изменяется в домене Windows." });
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
        }).RequireAuthorization("computers.view");
        app.MapPost("/api/computers/enroll", async (EnrollmentRequest request, HelperDb db) =>
        {
            string name = Security.Canonical(request.MachineName);
            if (!Security.MachineValid(name)) return Results.BadRequest(new { error = "Неверное имя ПК" });
            var computer = await db.Computers.SingleOrDefaultAsync(c => c.MachineName == name);
            if (computer?.IsOnline == true) return Results.Conflict(new { error = "Сначала отключите помощника для смены ключа" });
            if (computer == null) { computer = new Computer { MachineName = name }; db.Computers.Add(computer); }
            string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)); computer.AgentKeyHash = Security.KeyHash(key);
            await db.SaveChangesAsync(); return Results.Ok(new { machineName = name, clientToken = key });
        }).RequireAuthorization("computers.manage");
        app.MapGet("/api/computers/{id:int}/inventory", async (int id, HelperDb db) =>
        {
            var c = await db.Computers.FindAsync(id);
            return c == null ? Results.NotFound() : Results.Ok(new { c.MachineName, c.IpAddress, c.OsVersion, c.LastSeen,
                hardware = JsonSerializer.Deserialize<JsonElement>(c.HardwareJson), software = JsonSerializer.Deserialize<JsonElement>(c.SoftwareJson) });
        }).RequireAuthorization("inventory.view");
        app.MapDelete("/api/computers/{id:int}", async (int id, HelperDb db) =>
        {
            var c = await db.Computers.FindAsync(id); if (c == null) return Results.NotFound();
            if (c.IsOnline) return Results.Conflict(new { error = "Отключите ПК перед удалением регистрации" });
            db.Computers.Remove(c); await db.SaveChangesAsync(); return Results.NoContent();
        }).RequireAuthorization("computers.delete");
        app.MapGet("/api/scripts", (IConfiguration config) => Results.Ok(config.GetSection("Scripts").GetChildren().Select(s => new { id = s.Key, title = s["Title"] ?? s.Key }))).RequireAuthorization("commands.execute");
        app.MapPost("/api/commands", async (CommandRequest request, ClaimsPrincipal p, CommandService service, CancellationToken token) => Results.Ok(await service.SendAsync(p, request, token))).RequireAuthorization("commands.execute");
        app.MapGet("/api/tasks/{taskId}", async (string taskId, ClaimsPrincipal p, HelperDb db) =>
        {
            var task = await db.AuditLogs.AsNoTracking().SingleOrDefaultAsync(t => t.TaskId == taskId);
            return task == null || (!Access.Can(p, "audit.all") && task.AdminUsername != p.Identity!.Name) ? Results.NotFound() : Results.Ok(task);
        }).RequireAuthorization("commands.execute");

        app.MapGet("/api/buttons", async (HelperDb db) => Results.Ok(await db.Buttons.AsNoTracking().OrderBy(b => b.OrderIndex).ThenBy(b => b.Id).ToListAsync())).RequireAuthorization("buttons.manage");
        app.MapPost("/api/buttons", async (ActionButton button, ClaimsPrincipal p, HelperDb db) =>
        {
            ValidateButton(button, p); button.Id = 0; db.Buttons.Add(button); await db.SaveChangesAsync(); return Results.Ok(button);
        }).RequireAuthorization("buttons.manage");
        app.MapPut("/api/buttons/{id:int}", async (int id, ActionButton button, ClaimsPrincipal p, HelperDb db) =>
        {
            ValidateButton(button, p); var old = await db.Buttons.FindAsync(id); if (old == null) return Results.NotFound();
            old.Title = button.Title; old.ActionType = button.ActionType; old.Payload = button.Payload; old.OrderIndex = button.OrderIndex;
            old.IsActive = button.IsActive; old.IconName = button.IconName; old.TargetGroup = button.TargetGroup;
            await db.SaveChangesAsync(); return Results.Ok(old);
        }).RequireAuthorization("buttons.manage");
        app.MapDelete("/api/buttons/{id:int}", async (int id, HelperDb db) =>
        { var b = await db.Buttons.FindAsync(id); if (b == null) return Results.NotFound(); db.Remove(b); await db.SaveChangesAsync(); return Results.NoContent(); }).RequireAuthorization("buttons.manage");
        app.MapPost("/api/buttons/apply", async (HelperDb db, IHubContext<HelperHub> hub) =>
        {
            var computers = await db.Computers.AsNoTracking().Where(c => c.IsOnline && c.ConnectionId != null).ToListAsync();
            foreach (var c in computers) await hub.Clients.Client(c.ConnectionId!).SendAsync("OnButtonsUpdated", await HelperHub.Buttons(db, c));
            return Results.Ok(new { notified = computers.Count });
        }).RequireAuthorization("buttons.manage");

        app.MapGet("/api/permissions", () => Results.Ok(new { catalog = Access.Catalog, roles = Roles.All.ToDictionary(r => r, r => Access.Defaults(r)) })).RequireAuthorization("users.manage");
        app.MapGet("/api/users", async (HelperDb db) => Results.Ok(await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync())).RequireAuthorization("users.manage");
        app.MapPost("/api/users", async (UserRequest request, HelperDb db) =>
        {
            ValidateUser(request, true); string name = Security.Login(request.Username);
            if (await db.Users.AnyAsync(u => u.Username == name)) return Results.Conflict(new { error = "Логин уже существует" });
            var user = new PanelUser { Username = name, FullName = request.FullName.Trim(), Role = request.Role, IsActive = request.IsActive,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password!, 12), MustChangePassword = true, Permissions = request.Permissions ?? new() };
            db.Users.Add(user); await db.SaveChangesAsync(); return Results.Ok(user);
        }).RequireAuthorization("users.manage");
        app.MapPut("/api/users/{id:int}", async (int id, UserRequest request, HelperDb db, PanelSessions sessions) =>
        {
            ValidateUser(request, false); var user = await db.Users.FindAsync(id); if (user == null) return Results.NotFound();
            if (user.AuthSource == "AD" && (Security.Login(request.Username) != user.Username || !string.IsNullOrEmpty(request.Password))) return Results.BadRequest(new { error = "Логин и пароль AD изменяются в Active Directory." });
            if (user.Role == Roles.SuperAdmin && user.IsActive && (request.Role != Roles.SuperAdmin || !request.IsActive)) await ProtectLastSuper(db, user);
            string name = Security.Login(request.Username);
            if (await db.Users.AnyAsync(u => u.Id != id && u.Username == name)) return Results.Conflict(new { error = "Логин уже существует" });
            user.Username = name; user.FullName = request.FullName.Trim(); user.Role = request.Role; user.IsActive = request.IsActive; if (request.Permissions != null) user.Permissions = request.Permissions; user.SecurityVersion++;
            if (!string.IsNullOrEmpty(request.Password)) { user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, 12); user.MustChangePassword = true; }
            await db.SaveChangesAsync(); sessions.Revoke(user.Id); return Results.Ok(user);
        }).RequireAuthorization("users.manage");
        app.MapDelete("/api/users/{id:int}", async (int id, HelperDb db, PanelSessions sessions) =>
        {
            var user = await db.Users.FindAsync(id); if (user == null) return Results.NotFound();
            if (user.AuthSource == "AD") return Results.BadRequest(new { error = "Для запрета повторного входа AD отключите пользователя вместо удаления." });
            await ProtectLastSuper(db, user); db.Users.Remove(user); await db.SaveChangesAsync(); sessions.Revoke(id); return Results.NoContent();
        }).RequireAuthorization("users.manage");
        app.MapGet("/api/audit", async (ClaimsPrincipal p, HelperDb db) =>
        {
            var query = db.AuditLogs.AsNoTracking();
            bool terminal = Access.Can(p, "terminal.execute");
            if (!Access.Can(p, "audit.all")) query = query.Where(l => l.AdminUsername == p.Identity!.Name && (l.CommandType != "cmd" && l.CommandType != "powershell" || terminal));
            return Results.Ok(await query.OrderByDescending(l => l.Timestamp).Take(500).ToListAsync());
        }).RequireAuthorization("audit.view");
        app.MapDelete("/api/audit", async (HelperDb db) => { await db.AuditLogs.ExecuteDeleteAsync(); return Results.NoContent(); }).RequireAuthorization("audit.all");

        app.MapGet("/api/tickets", async (ClaimsPrincipal p, HelperDb db) =>
        {
            var query = db.Tickets.AsNoTracking();
            string owner = Security.TicketOwner(p);
            if (!Access.Can(p, "tickets.all")) query = query.Where(t => t.Username.ToLower() == owner);
            return Results.Ok(await query.OrderByDescending(t => t.CreatedAt).Take(500).ToListAsync());
        }).RequireAuthorization("Panel");
        app.MapPost("/api/tickets", async (TicketRequest request, ClaimsPrincipal p, HelperDb db, GlpiService glpi, IntegrationSettings settings, CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 8000 || request.Title.Length > 160) return Results.BadRequest();
            string user = Security.TicketOwner(p); int id = await glpi.CreateTicketAsync(user, "WebPanel", request.Description, token);
            var ticket = new TicketRecord { GlpiId = id, Username = user, MachineName = "WebPanel", Title = request.Title, Description = request.Description };
            db.Tickets.Add(ticket);
            if (settings.Telegram().Enabled) db.TelegramDeliveries.Add(new TelegramDelivery { Ticket = ticket });
            await db.SaveChangesAsync(token); return Results.Ok(ticket);
        }).RequireAuthorization("tickets.manage");
        app.MapGet("/api/tickets/{id:int}", async (int id, ClaimsPrincipal p, HelperDb db, GlpiService glpi, CancellationToken token) =>
        {
            var ticket = await db.Tickets.FindAsync(id); if (ticket == null) return Results.NotFound();
            if (!Access.Can(p, "tickets.all") && !ticket.Username.Equals(Security.TicketOwner(p), StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
            return Results.Ok(new { local = ticket, glpi = await glpi.GetTicketAsync(ticket.GlpiId, token) });
        }).RequireAuthorization("Panel");
        app.MapPut("/api/tickets/{id:int}/status", async (int id, TicketStatus request, HelperDb db, GlpiService glpi, CancellationToken token) =>
        {
            var ticket = await db.Tickets.FindAsync(id); if (ticket == null) return Results.NotFound();
            await glpi.UpdateTicketAsync(ticket.GlpiId, request.Status, token); ticket.Status = request.Status.ToString();
            await db.SaveChangesAsync(token); return Results.Ok(ticket);
        }).RequireAuthorization("tickets.manage");
    }
    private static async Task ProtectLastSuper(HelperDb db, PanelUser user)
    {
        if (user.IsActive && user.Role == Roles.SuperAdmin && await db.Users.CountAsync(u => u.IsActive && u.Role == Roles.SuperAdmin) <= 1)
            throw new ArgumentException("Нельзя удалить или отключить последнего SuperAdmin");
    }
    private static void ValidateUser(UserRequest request, bool requirePassword)
    {
        Access.Validate(request.Permissions);
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
        if (b.ActionType == "run_command" && !Access.Can(p, "terminal.execute")) throw new UnauthorizedAccessException("Не выдано право настройки командных кнопок");
        if (b.ActionType == "open_url" && (!Uri.TryCreate(b.Payload, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "search-ms")))
            throw new ArgumentException("Разрешены HTTP(S) и search-ms");
    }
    public record TicketStatus(int Status);
}
