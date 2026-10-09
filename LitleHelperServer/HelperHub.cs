using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace LitleHelperServer;

[Authorize(AuthenticationSchemes = "Bearer,Agent")]
public class HelperHub(HelperDb db, GlpiService glpi, CommandService commands, PanelSessions sessions, IntegrationSettings settings, TicketManagementGate ticketGate, IHttpClientFactory? httpFactory = null) : Hub
{
    public static object Status(Computer c) => new { c.Id, c.MachineName, c.DomainName, c.CurrentUser, c.IsOnline, c.LastSeen, c.BranchId, c.Room };
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
            await Groups.AddToGroupAsync(Context.ConnectionId, "AllAgents");
            await Groups.AddToGroupAsync(Context.ConnectionId, "AgentMachine:" + computer.MachineName.ToLowerInvariant());
            if (computer.BranchId != null)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, "BranchAgents:" + computer.BranchId);
            }
            if (!string.IsNullOrWhiteSpace(computer.CurrentUser))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, "AgentUser:" + computer.CurrentUser.ToLowerInvariant());
                await Groups.AddToGroupAsync(Context.ConnectionId, "AgentUser:" + Security.TicketUser(computer.CurrentUser).ToLowerInvariant());
            }
            await Clients.Group("PanelStaff").SendAsync("ComputerChanged", Status(computer));
        }
        else
        {
            var user = await db.Users.SingleAsync(u => u.Username == Context.User.Identity!.Name);
            if (!user.IsActive || user.MustChangePassword) { Context.Abort(); return; }
            var context = Context;
            sessions.Add(Context.ConnectionId, user.Id, context.Abort);
            if (Access.Can(user, "tickets.all"))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, user.Role == Roles.SuperAdmin ? "TicketStaffAll" : "TicketStaff:" + user.BranchId);
                await Groups.AddToGroupAsync(Context.ConnectionId, "TicketStaffLegacy");
            }
            if (Access.Can(user, "computers.view")) await Groups.AddToGroupAsync(Context.ConnectionId, "PanelStaff");
            if (Access.Can(user, "commands.execute")) await Groups.AddToGroupAsync(Context.ConnectionId, "Admin:" + user.Username);
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

        var allBranches = await db.Branches.AsNoTracking().Where(b => b.IsActive).ToListAsync();
        var matchedBranch = Branches.FindByComputerName(allBranches, c.MachineName);
        if (matchedBranch != null)
        {
            c.BranchId = matchedBranch.Id;
            if (!string.IsNullOrWhiteSpace(c.CurrentUser))
            {
                string ticketUser = Security.TicketUser(c.CurrentUser).ToLowerInvariant();
                var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == ticketUser || u.Username.ToLower() == c.CurrentUser.ToLower());
                if (user != null && user.Role != Roles.SuperAdmin && user.Role != Roles.Admin)
                {
                    user.BranchId = matchedBranch.Id;
                    if (!string.IsNullOrWhiteSpace(user.Room) && string.IsNullOrWhiteSpace(c.Room))
                        c.Room = user.Room;
                    else if (!string.IsNullOrWhiteSpace(c.Room) && string.IsNullOrWhiteSpace(user.Room))
                        user.Room = c.Room;
                }
            }
        }
        await db.SaveChangesAsync();

        await Groups.AddToGroupAsync(Context.ConnectionId, "AllAgents");
        await Groups.AddToGroupAsync(Context.ConnectionId, "AgentMachine:" + c.MachineName.ToLowerInvariant());
        if (c.BranchId != null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "BranchAgents:" + c.BranchId);
        }
        if (!string.IsNullOrWhiteSpace(c.CurrentUser))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "AgentUser:" + c.CurrentUser.ToLowerInvariant());
            await Groups.AddToGroupAsync(Context.ConnectionId, "AgentUser:" + Security.TicketUser(c.CurrentUser).ToLowerInvariant());
        }
        await Clients.Group("PanelStaff").SendAsync("ComputerChanged", Status(c));
        var buttons = await Buttons(db, c); await Clients.Caller.SendAsync("OnButtonsUpdated", buttons);
        await Clients.Caller.SendAsync("SuperAdminAvailable", await db.Users.AnyAsync(u => u.Role == Roles.SuperAdmin && u.IsActive && !u.MustChangePassword && u.AssistantMachine == c.MachineName));
        if (!string.IsNullOrWhiteSpace(c.Room))
            await Clients.Caller.SendAsync("ClientRoomUpdated", c.Room);
        return buttons;
    }
    public async Task UpdateClientRoom(string room)
    {
        var c = await Agent();
        if (room != null && (room.Length > 100 || room.Any(char.IsControl)))
            throw new HubException("Кабинет: до 100 символов без управляющих символов.");
        string cleanRoom = (room ?? "").Trim();
        c.Room = cleanRoom;
        if (!string.IsNullOrWhiteSpace(c.CurrentUser))
        {
            string ticketUser = Security.TicketUser(c.CurrentUser).ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == ticketUser || u.Username.ToLower() == c.CurrentUser.ToLower());
            if (user != null)
            {
                user.Room = cleanRoom;
            }
        }
        await db.SaveChangesAsync();
        await Clients.Group("PanelStaff").SendAsync("ComputerChanged", Status(c));
    }
    public async Task Heartbeat() { var c = await Agent(); c.LastSeen = DateTime.UtcNow; await db.SaveChangesAsync(); await Clients.Caller.SendAsync("SuperAdminAvailable", await db.Users.AnyAsync(u => u.Role == Roles.SuperAdmin && u.IsActive && !u.MustChangePassword && u.AssistantMachine == c.MachineName)); }
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
        => await CreateTicketAt(title, description, null, "");
    public async Task<List<Branch>> GetBranches()
    { await Agent(); return await db.Branches.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync(); }
    public async Task<int> CreateTicketAt(string title, string description, int? branchId, string room)
    {
        var c = await Agent();
        if (string.IsNullOrWhiteSpace(c.CurrentUser) || string.IsNullOrWhiteSpace(description) || description.Length > 8000 || title.Length > 160)
            throw new HubException("Введите текст проблемы (до 8000 символов)");
        await ticketGate.Semaphore.WaitAsync(Context.ConnectionAborted);
        try
        {
            string owner = Security.TicketUser(c.CurrentUser);
            var branch = await Branches.ValidateTicketAsync(db, branchId, room);
            int id = await glpi.CreateTicketAsync(owner, c.MachineName, Branches.Description(description, branch, room), Context.ConnectionAborted);
            var ticket = new TicketRecord { GlpiId = id, Username = owner, MachineName = c.MachineName, Title = title, Description = description, BranchId = branch?.Id, BranchName = branch?.Name ?? "", Room = room.Trim() };
            db.Tickets.Add(ticket);
            if (settings.Telegram().Enabled) db.TelegramDeliveries.Add(new TelegramDelivery { Ticket = ticket });
            await db.SaveChangesAsync();
            await Clients.Group("User:" + owner).SendAsync("TicketCreated", ticket);
            await Clients.Group("TicketStaffAll").SendAsync("TicketCreated", ticket);
            await Clients.Group(ticket.BranchId == null ? "TicketStaffLegacy" : "TicketStaff:" + ticket.BranchId).SendAsync("TicketCreated", ticket);
            return id;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException or ArgumentException)
        {
            string message = ex is InvalidOperationException or ArgumentException ? ex.Message : ex is TaskCanceledException ? "GLPI не ответил за 20 секунд. Заявка не отправлена." : "Сервер не смог подключиться к GLPI. Проверьте адрес, DNS и сеть в настройках GLPI.";
            throw new HubException(message);
        }
        finally { ticketGate.Semaphore.Release(); }
    }

    public async Task TriggerEmergencyAlert(string code, string cabinet, string? notes = null, string? imageBase64 = null, string? department = null)
    {
        var c = await Agent();
        var em = settings.Emergency();
        if (!em.AllowClientTrigger && !em.AllowStandalone) throw new HubException("Запуск экстренных кодов через помощника отключён");
        string upperCode = code.Trim().ToUpperInvariant();
        string title = upperCode switch
        {
            "CODE_RED" => "КОД КРАСНЫЙ: Пожар / Задымление",
            "CODE_BLACK" => "КОД ЧЁРНЫЙ: Угроза взрыва / Теракт",
            "CODE_ORANGE" => "КОД ОРАНЖЕВЫЙ: ЧС / Опасные вещества",
            "CODE_YELLOW" => "КОД ЖЁЛТЫЙ: Чрезвычайная ситуация",
            "CODE_BLUE" => "КОД СИНИЙ: Реанимация / Остановка сердца",
            "CODE_WHITE" => "КОД БЕЛЫЙ: Агрессия / Нападение",
            "CODE_PINK" => "КОД РОЗОВЫЙ: Потеря / Похищение ребёнка",
            "SPECIALIST_CALL" => string.IsNullOrWhiteSpace(department) ? "СРОЧНЫЙ ВЫЗОВ СПЕЦИАЛИСТА" : $"СРОЧНЫЙ ВЫЗОВ: {department.Trim()}",
            _ => "ЭКСТРЕННОЕ ОПОВЕЩЕНИЕ"
        };
        string activeCabinet = !string.IsNullOrWhiteSpace(cabinet) ? cabinet.Trim() : (!string.IsNullOrWhiteSpace(c.Room) ? c.Room : c.MachineName);
        int? callId = upperCode == "SPECIALIST_CALL" ? Random.Shared.Next(10000, 99999) : null;
        var notice = new EmergencyAlertNotice(upperCode, title, activeCabinet, notes?.Trim(), null, imageBase64, null, 300, callId, department);

        var allBranches = await db.Branches.AsNoTracking().Where(b => b.IsActive).ToListAsync();
        var branch = c.BranchId != null
            ? allBranches.FirstOrDefault(b => b.Id == c.BranchId)
            : Branches.FindByComputerName(allBranches, c.MachineName);

        if (upperCode == "CODE_BLUE")
        {
            var targetUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (em.CodeBlueResponsibleUsers?.Count > 0)
            {
                foreach (var u in em.CodeBlueResponsibleUsers.Where(s => !string.IsNullOrWhiteSpace(s)))
                    targetUsers.Add(u.Trim());
            }
            var blueDept = em.Departments?.FirstOrDefault(d => d.Name.Contains("Реаним", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("Синий", StringComparison.OrdinalIgnoreCase));
            if (blueDept?.ResponsibleUsers?.Count > 0)
            {
                foreach (var u in blueDept.ResponsibleUsers.Where(s => !string.IsNullOrWhiteSpace(s)))
                    targetUsers.Add(u.Trim());
            }

            if (targetUsers.Count > 0)
            {
                var specialists = await db.Users.AsNoTracking()
                    .Where(u => u.IsActive && targetUsers.Contains(u.Username))
                    .ToListAsync();

                var matchingLogins = specialists
                    .Where(u => branch == null || u.BranchId == null || u.BranchId == branch.Id || u.Role == Roles.SuperAdmin || u.Role == Roles.Admin)
                    .Select(u => u.Username.ToLowerInvariant())
                    .Distinct()
                    .ToList();

                if (matchingLogins.Count > 0)
                {
                    foreach (var login in matchingLogins)
                        await Clients.Group("AgentUser:" + login).SendAsync("EmergencyAlertNotice", notice);
                }
                else
                {
                    foreach (var u in targetUsers)
                        await Clients.Group("AgentUser:" + u.ToLowerInvariant()).SendAsync("EmergencyAlertNotice", notice);
                }
            }
            else
            {
                if (branch != null)
                    await Clients.Group("BranchAgents:" + branch.Id).SendAsync("EmergencyAlertNotice", notice);
                else
                    await Clients.Group("AllAgents").SendAsync("EmergencyAlertNotice", notice);
            }
        }
        else if (!string.IsNullOrWhiteSpace(department) && em.Departments?.Count > 0)
        {
            var dept = em.Departments.FirstOrDefault(d => d.Name.Equals(department, StringComparison.OrdinalIgnoreCase) || d.Id.ToString() == department);
            if (dept != null && dept.ResponsibleUsers.Count > 0)
            {
                foreach (var u in dept.ResponsibleUsers)
                    await Clients.Group("AgentUser:" + u.Trim().ToLowerInvariant()).SendAsync("EmergencyAlertNotice", notice);
            }
            else
            {
                if (branch != null)
                    await Clients.Group("BranchAgents:" + branch.Id).SendAsync("EmergencyAlertNotice", notice);
                else
                    await Clients.Group("AllAgents").SendAsync("EmergencyAlertNotice", notice);
            }
        }
        else
        {
            if (branch != null)
                await Clients.Group("BranchAgents:" + branch.Id).SendAsync("EmergencyAlertNotice", notice);
            else
                await Clients.Group("AllAgents").SendAsync("EmergencyAlertNotice", notice);
        }

        await Clients.Group("PanelStaff").SendAsync("EmergencyAlertTriggered", new { notice, branchId = branch?.Id, branchName = branch?.Name, machine = c.MachineName });

        db.AuditLogs.Add(new() { AdminUsername = c.CurrentUser, MachineName = c.MachineName, CommandType = "emergency_trigger", CommandPayload = upperCode + " / " + activeCabinet + " / " + (notes ?? "") + (!string.IsNullOrWhiteSpace(department) ? " / " + department : "") + (branch != null ? " / " + branch.Name : ""), Status = "Completed", Result = "Оповещение разослано агентам" });
        await db.SaveChangesAsync();

        if (em.Enabled && !string.IsNullOrWhiteSpace(em.ServerUrl) && httpFactory != null)
        {
            try
            {
                var http = httpFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(3);
                var endpoint = em.ServerUrl.TrimEnd('/') + "/api/broadcast/client/alert";
                var body = new { code = upperCode, cabinet = activeCabinet, notes = notes?.Trim(), client_name = c.MachineName, image = imageBase64, department, branch_name = branch?.Name };
                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                if (!string.IsNullOrWhiteSpace(em.ApiKey)) req.Headers.Add("X-Client-Key", em.ApiKey);
                req.Content = System.Net.Http.Json.JsonContent.Create(body);
                _ = http.SendAsync(req);
            }
            catch { }
        }
    }

    public Task<List<string>> GetSpecialistDepartments()
    {
        var em = settings.Emergency();
        var depts = em.Departments?.Select(d => d.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList() ?? new();
        if (depts.Count == 0)
        {
            depts = new List<string> { "Дежурный врач", "Реаниматолог", "Хирург", "Травматолог", "Охрана / Служба безопасности" };
        }
        return Task.FromResult(depts);
    }

    public async Task AcknowledgeSpecialistCall(int callId, bool accepted, string? reason)
    {
        var c = await Agent();
        var em = settings.Emergency();
        db.AuditLogs.Add(new() { AdminUsername = c.CurrentUser, MachineName = c.MachineName, CommandType = "specialist_call_ack", CommandPayload = $"Call #{callId}, Accepted: {accepted}, Reason: {reason}", Status = "Completed", Result = $"Специалист {c.CurrentUser} ({c.MachineName}) {(accepted ? "подтвердил" : "отклонил")} вызов" });
        await db.SaveChangesAsync();

        if (em.Enabled && !string.IsNullOrWhiteSpace(em.ServerUrl) && httpFactory != null)
        {
            try
            {
                var http = httpFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(3);
                var endpoint = $"{em.ServerUrl.TrimEnd('/')}/api/specialist/calls/{callId}/ack";
                var body = new { acknowledged_by = $"{c.CurrentUser} ({c.MachineName})" };
                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                if (!string.IsNullOrWhiteSpace(em.ApiKey)) req.Headers.Add("X-Client-Key", em.ApiKey);
                req.Content = System.Net.Http.Json.JsonContent.Create(body);
                _ = http.SendAsync(req);
            }
            catch { }
        }
    }
}

