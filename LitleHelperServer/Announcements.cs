using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;

namespace LitleHelperServer;

public record ClientNotice(string Text, string Sender = "", int DurationSeconds = 30);
public record NoticeRequest(string[] Machines, string Text, int DurationSeconds = 30);
public class SuperAdminButton
{
    public int Id { get; set; }
    public string Title { get; set; } = "Сообщение клиентам";
    public string Message { get; set; } = "";
    public string Target { get; set; } = "select";
    public int DurationSeconds { get; set; } = 30;
    public int OrderIndex { get; set; }
    public bool IsActive { get; set; } = true;
}
public class AnnouncementService(HelperDb db, CommandService commands, IHubContext<HelperHub> hub)
{
    public static void Validate(string text, int duration)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1000 || duration is < 10 or > 300 || text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new ArgumentException("Сообщение: 1–1000 символов, показ: 10–300 секунд.");
    }
    public static void ValidateButton(SuperAdminButton b)
    {
        if (string.IsNullOrWhiteSpace(b.Title) || b.Title.Length > 100 || b.Message.Length > 1000 || b.Target is not ("all" or "select") || b.DurationSeconds is < 10 or > 300 || b.OrderIndex is < 0 or > 10000)
            throw new ArgumentException("Проверьте название, сообщение, получателей и длительность кнопки.");
        if (b.Message.Length > 0) Validate(b.Message, b.DurationSeconds);
    }
    public async Task<string> BindingAsync(string? machine, string role, ClaimsPrincipal principal, CancellationToken token)
    {
        if (machine == null || machine.Trim().Length == 0) return "";
        if (!principal.IsInRole(Roles.SuperAdmin)) throw new UnauthorizedAccessException("Привязку помощника изменяет только SuperAdmin");
        if (role != Roles.SuperAdmin) throw new ArgumentException("Помощника можно привязать только к SuperAdmin");
        string name = Security.Canonical(machine);
        if (!Security.MachineValid(name) || !await db.Computers.AnyAsync(c => c.MachineName == name, token)) throw new ArgumentException("Выберите зарегистрированный компьютер");
        return name;
    }
    public async Task AuthorizeClientAsync(ClaimsPrincipal principal, HttpRequest request, CancellationToken token)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Username == principal.Identity!.Name, token);
        string machine = Security.Canonical(request.Headers["X-Machine-Name"].ToString());
        string key = request.Headers["X-Client-Key"].ToString();
        if (user == null || user.Role != Roles.SuperAdmin || !user.IsActive || user.MustChangePassword ||
            !principal.HasClaim("version", user.SecurityVersion.ToString()) || user.AssistantMachine.Length == 0 || user.AssistantMachine != machine || key.Length is < 32 or > 200)
            throw new UnauthorizedAccessException("Помощник не привязан к вашей учётной записи SuperAdmin");
        var computer = await db.Computers.AsNoTracking().SingleOrDefaultAsync(c => c.MachineName == machine, token);
        if (computer == null || !computer.IsOnline || computer.AgentKeyHash.Length == 0 ||
            !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(computer.AgentKeyHash), System.Text.Encoding.UTF8.GetBytes(Security.KeyHash(key))))
            throw new UnauthorizedAccessException("Неверный ключ или помощник не подключён");
    }
    public Task<List<AuditLog>> SendAsync(ClaimsPrincipal principal, NoticeRequest request, CancellationToken token)
    {
        if (!principal.IsInRole(Roles.SuperAdmin)) throw new UnauthorizedAccessException("Сообщения доступны только SuperAdmin");
        Validate(request.Text, request.DurationSeconds);
        if (request.Machines == null || request.Machines.Length is < 1 or > 500) throw new ArgumentException("Выберите получателей");
        if (request.Machines.Any(m => string.IsNullOrWhiteSpace(m))) throw new ArgumentException("Неверный получатель");
        if (request.Machines.Any(m => Security.Canonical(m) == "ALL") && request.Machines.Length != 1) throw new ArgumentException("ALL должен быть единственным получателем");
        return commands.SendAsync(principal, new(request.Machines, "notice", JsonSerializer.Serialize(new ClientNotice(request.Text.Trim(), "", request.DurationSeconds))), token);
    }
    public async Task RefreshAvailabilityAsync(CancellationToken token)
    {
        var names = await db.Users.AsNoTracking().Where(u => u.Role == Roles.SuperAdmin && u.IsActive && !u.MustChangePassword && u.AssistantMachine != "").Select(u => u.AssistantMachine).ToListAsync(token);
        var computers = await db.Computers.AsNoTracking().Where(c => c.IsOnline && c.ConnectionId != null).ToListAsync(token);
        foreach (var c in computers) await hub.Clients.Client(c.ConnectionId!).SendAsync("SuperAdminAvailable", names.Contains(c.MachineName), token);
    }
    public static async Task EnsureSchemaAsync(HelperDb db)
    {
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"AssistantMachine\" TEXT NOT NULL DEFAULT ''");
        else
        {
            await db.Database.OpenConnectionAsync(); using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = "PRAGMA table_info('Users')";
            bool exists = false; using (var reader = await command.ExecuteReaderAsync()) while (await reader.ReadAsync()) if (reader.GetString(1) == "AssistantMachine") exists = true;
            if (!exists) await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN \"AssistantMachine\" TEXT NOT NULL DEFAULT ''");
            await db.Database.CloseConnectionAsync();
        }
        string id = db.Database.IsNpgsql() ? "INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY" : "INTEGER PRIMARY KEY AUTOINCREMENT";
        string boolean = db.Database.IsNpgsql() ? "BOOLEAN" : "INTEGER";
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS \"SuperAdminButtons\" (\"Id\" " + id + ", \"Title\" TEXT NOT NULL, \"Message\" TEXT NOT NULL, \"Target\" TEXT NOT NULL, \"DurationSeconds\" INTEGER NOT NULL, \"OrderIndex\" INTEGER NOT NULL, \"IsActive\" " + boolean + " NOT NULL)");
    }
}
public static class AnnouncementApi
{
    public static void MapAnnouncementApi(this WebApplication app)
    {
        app.MapPost("/api/messages", async (NoticeRequest request, ClaimsPrincipal p, AnnouncementService service, CancellationToken token) => Results.Ok(await service.SendAsync(p, request, token))).RequireAuthorization("Super");
        app.MapGet("/api/super-buttons", async (HelperDb db) => Results.Ok(await db.SuperAdminButtons.AsNoTracking().OrderBy(b => b.OrderIndex).ThenBy(b => b.Id).ToListAsync())).RequireAuthorization("Super");
        app.MapPost("/api/super-buttons", async (SuperAdminButton button, HelperDb db) =>
        {
            AnnouncementService.ValidateButton(button); button.Id = 0; db.SuperAdminButtons.Add(button); await db.SaveChangesAsync(); return Results.Ok(button);
        }).RequireAuthorization("Super");
        app.MapPut("/api/super-buttons/{id:int}", async (int id, SuperAdminButton button, HelperDb db) =>
        {
            AnnouncementService.ValidateButton(button); var old = await db.SuperAdminButtons.FindAsync(id); if (old == null) return Results.NotFound();
            old.Title = button.Title; old.Message = button.Message; old.Target = button.Target; old.DurationSeconds = button.DurationSeconds; old.OrderIndex = button.OrderIndex; old.IsActive = button.IsActive;
            await db.SaveChangesAsync(); return Results.Ok(old);
        }).RequireAuthorization("Super");
        app.MapDelete("/api/super-buttons/{id:int}", async (int id, HelperDb db) => { var button = await db.SuperAdminButtons.FindAsync(id); if (button == null) return Results.NotFound(); db.Remove(button); await db.SaveChangesAsync(); return Results.NoContent(); }).RequireAuthorization("Super");
        app.MapPost("/api/super-buttons/apply", async (AnnouncementService service, CancellationToken token) => { await service.RefreshAvailabilityAsync(token); return Results.Ok(new { success = true }); }).RequireAuthorization("Super");
        app.MapGet("/api/super-client/buttons", async (ClaimsPrincipal p, HttpRequest request, HelperDb db, AnnouncementService service, CancellationToken token) =>
        {
            await service.AuthorizeClientAsync(p, request, token); return Results.Ok(await db.SuperAdminButtons.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.OrderIndex).ThenBy(b => b.Id).Take(12).ToListAsync(token));
        }).RequireAuthorization("Super");
        app.MapGet("/api/super-client/computers", async (ClaimsPrincipal p, HttpRequest request, HelperDb db, AnnouncementService service, CancellationToken token) =>
        {
            await service.AuthorizeClientAsync(p, request, token); return Results.Ok((await db.Computers.AsNoTracking().OrderBy(c => c.MachineName).ToListAsync(token)).Select(HelperHub.Status));
        }).RequireAuthorization("Super");
        app.MapPost("/api/super-client/send", async (NoticeRequest body, ClaimsPrincipal p, HttpRequest request, AnnouncementService service, CancellationToken token) =>
        {
            await service.AuthorizeClientAsync(p, request, token); return Results.Ok(await service.SendAsync(p, body, token));
        }).RequireAuthorization("Super");
    }
}
