using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace LitleHelperServer;

[Authorize(AuthenticationSchemes = "Bearer,Agent")]
public class HelperHub(HelperDb db, GlpiService glpi, CommandService commands, PanelSessions sessions, IntegrationSettings settings) : Hub
{
    public static object Status(Computer c) => new { c.Id, c.MachineName, c.DomainName, c.CurrentUser, c.IsOnline, c.LastSeen };
    public static bool Applies(ActionButton b, Computer c) => b.TargetGroup.Equals("All", StringComparison.OrdinalIgnoreCase) ||
        b.TargetGroup.Equals("domain:" + c.DomainName, StringComparison.OrdinalIgnoreCase) || b.TargetGroup.Equals("pc:" + c.MachineName, StringComparison.OrdinalIgnoreCase);
    public static async Task<List<ActionButton>> Buttons(HelperDb db, Computer c) =>
        (await db.Buttons.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.OrderIndex).ThenBy(b => b.Id).ToListAsync()).Where(b => Applies(b, c)).Take(12).ToList();
    private async Task<Computer> Agent()
    {
        if (!Context.User!.IsInRole("Agent")) throw new HubException("Метод доступен только помощнику");
        return await db.Computers.SingleOrDefaultAsync(c => c.MachineName == Context.User.Identity!.Name && c.ConnectionId == Context.ConnectionId)
            ?? throw new HubException("Компьютер не зарегистрирован на этом соединении");
    }
    public override async Task OnConnectedAsync()
    {
        if (Context.User!.IsInRole("Agent"))
        {
            var computer = await db.Computers.SingleAsync(c => c.MachineName == Context.User.Identity!.Name);
            computer.IsOnline = true; computer.ConnectionId = Context.ConnectionId; computer.LastSeen = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await Groups.AddToGroupAsync(Context.ConnectionId, "All");
            await Clients.Group("PanelStaff").SendAsync("ComputerChanged", Status(computer));
        }
        else
        {
            var user = await db.Users.SingleAsync(u => u.Username == Context.User.Identity!.Name);
            if (!user.IsActive || user.MustChangePassword) { Context.Abort(); return; }
            var context = Context;
            sessions.Add(Context.ConnectionId, user.Id, context.Abort);
            if (user.Role != Roles.User) await Groups.AddToGroupAsync(Context.ConnectionId, "PanelStaff");
            if (user.Role is Roles.SuperAdmin or Roles.Admin) await Groups.AddToGroupAsync(Context.ConnectionId, "Admin:" + user.Username);
            await Groups.AddToGroupAsync(Context.ConnectionId, "User:" + Security.TicketOwner(Context.User));
        }
        await base.OnConnectedAsync();
    }
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        sessions.Remove(Context.ConnectionId);
        if (Context.User!.IsInRole("Agent"))
        {
            var computer = await db.Computers.SingleOrDefaultAsync(c => c.ConnectionId == Context.ConnectionId);
            if (computer != null)
            {
                computer.IsOnline = false; computer.ConnectionId = null; computer.LastSeen = DateTime.UtcNow;
                await db.SaveChangesAsync(); await Clients.Group("PanelStaff").SendAsync("ComputerChanged", Status(computer));
            }
        }
        await base.OnDisconnectedAsync(exception);
    }
    public async Task<List<ActionButton>> RegisterComputer(MachineInfo info)
    {
        var c = await Agent();
        if (Security.Canonical(info.MachineName) != c.MachineName || info.UserName.Length is < 1 or > 100 || info.DomainName.Length > 100 || info.OsVersion.Length > 300 || info.IpAddress.Length > 100)
            throw new HubException("Неверная регистрация");
        c.CurrentUser = Security.Login(info.UserName); c.DomainName = info.DomainName; c.IpAddress = info.IpAddress;
        c.OsVersion = info.OsVersion; c.LastSeen = DateTime.UtcNow;
        await db.SaveChangesAsync(); await Clients.Group("PanelStaff").SendAsync("ComputerChanged", Status(c));
        var buttons = await Buttons(db, c); await Clients.Caller.SendAsync("OnButtonsUpdated", buttons); return buttons;
    }
    public async Task Heartbeat() { var c = await Agent(); c.LastSeen = DateTime.UtcNow; await db.SaveChangesAsync(); }
    public async Task UpdateHardwareAndSoftware(JsonElement hardware, JsonElement software)
    {
        var c = await Agent();
        if (hardware.ValueKind != JsonValueKind.Object || software.ValueKind != JsonValueKind.Array || hardware.GetRawText().Length > 262144 || software.GetRawText().Length > 1048576)
            throw new HubException("Неверный инвентарь");
        c.HardwareJson = hardware.GetRawText(); c.SoftwareJson = software.GetRawText(); c.LastSeen = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
    public async Task<List<AuditLog>> SendCommandToClient(string machineName, string commandType, string payload)
    {
        try { return await commands.SendAsync(Context.User!, new([machineName], commandType, payload), Context.ConnectionAborted); }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException) { throw new HubException(ex.Message); }
    }
    public async Task SendExecutionOutput(string taskId, string output)
    {
        var c = await Agent();
        var log = await db.AuditLogs.SingleOrDefaultAsync(x => x.TaskId == taskId && x.MachineName == c.MachineName && x.Status == "Pending");
        if (log == null || output.Length > 8192) throw new HubException("Неверный результат задачи");
        if (log.Result.Length + output.Length <= 131072) { log.Result += output; await db.SaveChangesAsync(); }
        await Clients.Group("Admin:" + log.AdminUsername).SendAsync("ExecutionOutput", new { taskId, machineName = c.MachineName, output });
    }
    public async Task SendExecutionResult(string taskId, string output, int exitCode)
    {
        var c = await Agent();
        var log = await db.AuditLogs.SingleOrDefaultAsync(x => x.TaskId == taskId && x.MachineName == c.MachineName && x.Status == "Pending");
        if (log == null || output.Length > 131072) throw new HubException("Неверный результат задачи");
        log.Result = output; log.ExitCode = exitCode; log.Status = exitCode == 0 ? "Completed" : "Failed";
        await db.SaveChangesAsync();
        await Clients.Group("Admin:" + log.AdminUsername).SendAsync("ExecutionResult", log);
    }
    public async Task<int> CreateTicket(string title, string description)
    {
        var c = await Agent();
        if (string.IsNullOrWhiteSpace(c.CurrentUser) || string.IsNullOrWhiteSpace(description) || description.Length > 8000 || title.Length > 160)
            throw new HubException("Введите текст проблемы (до 8000 символов)");
        try
        {
            string owner = Security.TicketUser(c.CurrentUser);
            int id = await glpi.CreateTicketAsync(owner, c.MachineName, description, Context.ConnectionAborted);
            var ticket = new TicketRecord { GlpiId = id, Username = owner, MachineName = c.MachineName, Title = title, Description = description };
            db.Tickets.Add(ticket);
            if (settings.Telegram().Enabled) db.TelegramDeliveries.Add(new TelegramDelivery { Ticket = ticket });
            await db.SaveChangesAsync();
            await Clients.Group("User:" + owner).SendAsync("TicketCreated", ticket);
            await Clients.Group("PanelStaff").SendAsync("TicketCreated", ticket);
            return id;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            string message = ex is InvalidOperationException ? ex.Message : ex is TaskCanceledException ? "GLPI не ответил за 20 секунд. Заявка не отправлена." : "Сервер не смог подключиться к GLPI. Проверьте адрес, DNS и сеть в настройках GLPI.";
            throw new HubException(message);
        }
    }
}
