using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace LitleHelperServer;

[Authorize(AuthenticationSchemes = "Bearer,Agent")]
public partial class HelperHub(HelperDb db, GlpiService glpi, CommandService commands, PanelSessions sessions, IntegrationSettings settings, TicketManagementGate ticketGate, IConfiguration? configuration = null, IHttpClientFactory? httpFactory = null) : Hub
{
    public static object Status(Computer c) => new { c.Id, c.MachineName, c.DomainName, c.CurrentUser, c.IsOnline, c.LastSeen, c.BranchId, c.Room };
    public static bool Applies(ActionButton b, Computer c) => b.TargetGroup.Equals("All", StringComparison.OrdinalIgnoreCase) ||
        b.TargetGroup.Equals("domain:" + c.DomainName, StringComparison.OrdinalIgnoreCase) || b.TargetGroup.Equals("pc:" + c.MachineName, StringComparison.OrdinalIgnoreCase);
    public static async Task<List<ActionButton>> Buttons(HelperDb db, Computer c) =>
        (await db.Buttons.AsNoTracking().Where(b => (b.IsActive || b.IsSystem) && (!b.IsSystem || c.SupportsSystemButtons)).OrderBy(b => b.OrderIndex).ThenBy(b => b.Id).ToListAsync()).Where(b => Applies(b, c)).GroupBy(b => b.IsSystem).SelectMany(g => g.Key ? g : g.Take(12)).ToList();
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
            await Clients.Group("PanelStaff").SendAsync("ComputerChanged", Status(computer));
        }
        else
        {
            var user = await db.Users.SingleAsync(u => u.Username == Context.User.Identity!.Name);
            if (!user.IsActive || user.MustChangePassword) { Context.Abort(); return; }
            var context = Context;
            sessions.Add(Context.ConnectionId, user.Id, context.Abort);
            if (Access.Can(user,"reports.glpi.view") && Access.Can(user,"reports.glpi.settings"))
                await Groups.AddToGroupAsync(Context.ConnectionId,"SurveyAdmin:"+user.Id);
            if (Access.Can(user, "tickets.all"))
            {
                if (user.Role == Roles.SuperAdmin || (user.Role == Roles.Admin && user.BranchIds.Length == 0 && user.BranchId == null))
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, "TicketStaffAll");
                }
                else
                {
                    var staffBranches = user.BranchIds;
                    if (staffBranches.Length > 0)
                    {
                        foreach (var bId in staffBranches)
                            await Groups.AddToGroupAsync(Context.ConnectionId, "TicketStaff:" + bId);
                    }
                    else if (user.BranchId.HasValue)
                    {
                        await Groups.AddToGroupAsync(Context.ConnectionId, "TicketStaff:" + user.BranchId);
                    }
                }
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
        AgentUserSessions.Remove(Context.ConnectionId);
        if (Context.User!.IsInRole("Agent"))
        {
            var computer = await db.Computers.AsNoTracking().SingleOrDefaultAsync(c => c.ConnectionId == Context.ConnectionId);
            if (computer != null)
            {
                int changed=await db.Computers.Where(c=>c.Id==computer.Id && c.ConnectionId==Context.ConnectionId)
                    .ExecuteUpdateAsync(set=>set.SetProperty(c=>c.IsOnline,false).SetProperty(c=>c.ConnectionId,(string?)null).SetProperty(c=>c.LastSeen,DateTime.UtcNow));
                if(changed>0) {
                    EmergencyDirectorySync.Request();
                    var current=await db.Computers.AsNoTracking().SingleAsync(c=>c.Id==computer.Id);
                    await Clients.Group("PanelStaff").SendAsync("ComputerChanged",Status(current));
                }
            }
        }
        await base.OnDisconnectedAsync(exception);
    }
    public async Task<List<ActionButton>> RegisterComputer(MachineInfo info)
    {
        var c = await Agent();
        if (Security.Canonical(info.MachineName) != c.MachineName || info.UserName.Length is < 1 or > 100 || info.DomainName.Length > 100 || info.OsVersion.Length > 300 || info.IpAddress.Length > 100)
            throw new HubException("Неверная регистрация");
        int? previousBranch=c.BranchId;
        string previousUser=Security.TicketUser(c.CurrentUser).ToLowerInvariant();
        string reportedUser=Security.Login(info.UserName);
        if(!Security.TicketUser(reportedUser).Equals(previousUser,StringComparison.OrdinalIgnoreCase)) {
            AgentUserSessions.Remove(Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId,"AgentUser:"+previousUser);
        }
        c.CurrentUser = reportedUser;
        c.CurrentUserFullName = !string.IsNullOrWhiteSpace(info.UserFullName) ? info.UserFullName.Trim() : "";
        c.DomainName = info.DomainName; c.IpAddress = info.IpAddress;
        c.SupportsSystemButtons = info.SupportsSystemButtons;
        c.SupportsAiDiagnostics = info.SupportsAiDiagnostics;
        c.OsVersion = info.OsVersion; c.LastSeen = DateTime.UtcNow;

        var allBranches = await db.Branches.AsNoTracking().Where(b => b.IsActive).ToListAsync();
        var matchedBranch = Branches.FindByComputerName(allBranches, c.MachineName, c.IpAddress);
        if (matchedBranch != null)
        {
            c.BranchId = matchedBranch.Id;

        }
        await db.SaveChangesAsync(); EmergencyDirectorySync.Request();

        if(previousBranch.HasValue && previousBranch!=c.BranchId) await Groups.RemoveFromGroupAsync(Context.ConnectionId,"BranchAgents:"+previousBranch);
        await Groups.AddToGroupAsync(Context.ConnectionId, "AllAgents");
        await Groups.AddToGroupAsync(Context.ConnectionId, "AgentMachine:" + c.MachineName.ToLowerInvariant());
        if (c.BranchId != null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "BranchAgents:" + c.BranchId);
        }
        await Clients.Group("PanelStaff").SendAsync("ComputerChanged", Status(c));
        var buttons = await Buttons(db, c); await Clients.Caller.SendAsync("OnButtonsUpdated", buttons);
        await Clients.Caller.SendAsync("SuperAdminAvailable", await db.Users.AnyAsync(u => u.Role == Roles.SuperAdmin && u.IsActive && !u.MustChangePassword && u.AssistantMachine == c.MachineName));
        await Clients.Caller.SendAsync("WebGuardToggled", settings.Security().WebGuardEnabled);
        if (!string.IsNullOrWhiteSpace(c.Room))
            await Clients.Caller.SendAsync("ClientRoomUpdated", c.Room);
        return buttons;
    }
    public async Task UpdateClientRoom(string room)
    {
        var c = await Agent();
        if (room != null && (room.Length > 50 || room.Any(char.IsControl)))
            throw new HubException("Кабинет: до 50 символов без управляющих символов.");
        string cleanRoom = (room ?? "").Trim();
        c.Room = cleanRoom;
        if (await AgentUserSessions.UserAsync(c,db) != null)
        {
            string ticketUser = Security.TicketUser(c.CurrentUser).ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == ticketUser || u.Username.ToLower() == c.CurrentUser.ToLower());
            if (user != null)
            {
                user.Room = cleanRoom;
            }
        }
        await db.SaveChangesAsync(); EmergencyDirectorySync.Request();
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
    public async Task<List<string>> GetEmergencyCodes()
    {
        var computer=await Agent();
        var user=await AgentUserSessions.UserAsync(computer,db);
        return EmergencyLanguages.AllowedForRole(settings.Emergency(),user?.Role??Roles.User);
    }
    public async Task<List<MedicalStatusConfig>> GetMedicalStatuses()
    { await Agent(); return await db.MedicalStatuses.AsNoTracking().OrderBy(s => s.OrderIndex).ToListAsync(); }
    public async Task<object> GetTicketContext()
    {
        var computer = await Agent();
        var branches = await db.Branches.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();
        var detected = branches.FirstOrDefault(b => b.Id == computer.BranchId)
            ?? Branches.FindByComputerName(branches, computer.MachineName, computer.IpAddress);
        return new { branches, detectedBranchId = detected?.Id, room = computer.Room };
    }
    public async Task<List<KnowledgeArticle>> GetKnowledgeArticles(string? category = null, string? search = null)
    {
        var computer=await Agent();
        var q = KnowledgeBase.Visible(db,computer.BranchId,(await AgentUserSessions.UserAsync(computer,db))?.Role??Roles.User);
        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("all", StringComparison.OrdinalIgnoreCase))
            q = q.Where(a => a.Category == category);
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim().ToLower();
            q = q.Where(a => a.Title.ToLower().Contains(term) || a.Keywords.ToLower().Contains(term) || a.ContentMarkdown.ToLower().Contains(term));
        }
        return await q.OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.UpdatedAt).ToListAsync();
    }
    public async Task<KnowledgeArticle?> GetKnowledgeArticle(int id)
    {
        var computer=await Agent();
        var article = await KnowledgeBase.Visible(db,computer.BranchId,(await AgentUserSessions.UserAsync(computer,db))?.Role??Roles.User).SingleOrDefaultAsync(a=>a.Id==id);
        if (article != null)
        {
            article.ViewCount++;
            await db.KnowledgeArticles.Where(a=>a.Id==id).ExecuteUpdateAsync(set=>set.SetProperty(a=>a.ViewCount,a=>a.ViewCount+1));
        }
        return article;
    }
    public async Task<int> CreateTicketAt(string title, string description, int? branchId, string room, string? userFullName = null)
        => await CreateTicketAtWithFiles(title, description, branchId, room, userFullName, null);

    public Task<int> CreateTicketAtWithFiles(string title, string description, int? branchId, string room, string? userFullName = null, List<TicketAttachmentDto>? attachments = null)
        => CreateTicketCore(title,description,branchId,room,userFullName,attachments,false);
    public Task<int> CreateUrgentTicketAtWithFiles(string title, string description, int? branchId, string room, string? userFullName, List<TicketAttachmentDto>? attachments)
        => CreateTicketCore(title,description,branchId,room,userFullName,attachments,true);
    private async Task<int> CreateTicketCore(string title, string description, int? branchId, string room, string? userFullName, List<TicketAttachmentDto>? attachments, bool urgent)
    {
        var c = await Agent();
        room = CabinetValidation.Resolve(room, c.Room, c.MachineName);
        if (string.IsNullOrWhiteSpace(c.CurrentUser) || string.IsNullOrWhiteSpace(description) || description.Length > 8000 || title.Length > 160)
            throw new HubException("Введите текст проблемы (до 8000 символов)");
        var surveyUser = await AgentUserSessions.UserAsync(c,db);
        if (surveyUser != null) await SurveyService.EnsureCanCreate(surveyUser,urgent,Context.ConnectionAborted);
        if (urgent) description = "[Срочное обращение]\n\n" + description;
        await ticketGate.Semaphore.WaitAsync(Context.ConnectionAborted);
        try
        {
            var verifiedUser=await AgentUserSessions.UserAsync(c,db);
            string reportedOwner=Security.TicketUser(c.CurrentUser);
            string owner = verifiedUser!=null?Security.TicketUser(verifiedUser.Username):"unverified:"+c.MachineName;
            if(verifiedUser==null)description="[Windows-сессия не подтверждена; пользователь со слов агента: "+reportedOwner+"]\n\n"+description;
            string fullName = verifiedUser!=null?verifiedUser.FullName:!string.IsNullOrWhiteSpace(userFullName)
                ? userFullName.Trim()
                : (!string.IsNullOrWhiteSpace(c.CurrentUserFullName)
                    ? c.CurrentUserFullName
                    : (await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username.ToLower() == owner.ToLower()))?.FullName ?? "");

            bool isTempUser = Security.IsTemporaryUser(reportedOwner);
            if (isTempUser)
            {
                if (!description.Contains("[Временный профиль", StringComparison.OrdinalIgnoreCase))
                    description = "[ВНИМАНИЕ: На ПК зафиксирован временный профиль Windows (TEMP)]\n\n" + description;
                if (!title.StartsWith("[TEMP", StringComparison.OrdinalIgnoreCase))
                    title = "[TEMP] " + title;
                if (title.Length > 160) title = title[..160];
            }

            var branch = await Branches.ValidateTicketAsync(db, branchId, room);
            int id = await glpi.CreateTicketAsync(owner, c.MachineName, Branches.Description(description, branch, room), attachments, Context.ConnectionAborted);

            // Атомарно продвигаем курсор импорта GLPI, чтобы фоновый воркер не импортировал повторно созданную помощником заявку
            try
            {
                string stateKey = GlpiTicketImport.StateKey(glpi.ImportSource);
                var cursor = await db.TelegramBotStates.FindAsync([stateKey], Context.ConnectionAborted);
                if (cursor != null)
                {
                    if (id > cursor.Offset) cursor.Offset = id;
                }
                else
                {
                    db.TelegramBotStates.Add(new TelegramBotState { Id = stateKey, Offset = id });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GlpiCursor] Ошибка обновления курсора GLPI: {ex.Message}");
            }

            var ticket = new TicketRecord { GlpiId = id, Username = owner, UserFullName = fullName, MachineName = c.MachineName, Title = title, Description = description, BranchId = branch?.Id, BranchName = branch?.Name ?? "", Room = room.Trim() };
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync(Context.ConnectionAborted);

            if (attachments != null && attachments.Count > 0)
            {
                foreach (var att in attachments.Take(TicketAttachments.MaxAttachmentsPerTicket))
                {
                    if (att.Content != null && att.Content.Length > 0 && att.Content.Length <= TicketAttachments.MaxFileSize)
                    {
                        try
                        {
                            await TicketAttachments.SaveAsync(db, configuration, ticket.Id, att.FileName, att.Content, Context.ConnectionAborted);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Attachment] Ошибка сохранения файла {att.FileName} для заявки #{ticket.Id}: {ex.Message}");
                        }
                    }
                }
            }

            if (settings.Telegram().Enabled) db.TelegramDeliveries.Add(new TelegramDelivery { Ticket = ticket });
            await db.SaveChangesAsync(Context.ConnectionAborted);
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
        if (!(await GetEmergencyCodes()).Contains(upperCode)) throw new HubException("Этот код недоступен для вашей роли или отключён администратором.");
        string activeCabinet = CabinetValidation.Resolve(cabinet, c.Room, c.MachineName);
        int? callId = null;
        var notice = new EmergencyAlertNotice(upperCode, title, activeCabinet, notes?.Trim(), null, imageBase64, null, 300, callId, department, EmergencyLanguages.ForCode(em, upperCode));

        var allBranches = await db.Branches.AsNoTracking().Where(b => b.IsActive).ToListAsync();
        var branch = c.BranchId != null
            ? allBranches.FirstOrDefault(b => b.Id == c.BranchId)
            : Branches.FindByComputerName(allBranches, c.MachineName, c.IpAddress);

        var delivery=await EmergencyEvents.DispatchAsync(notice, branch?.Id, branch?.Name, c.CurrentUser,c.MachineName,db, settings, Clients, Context.ConnectionAborted);
        await Clients.Group("PanelStaff").SendAsync("EmergencyAlertTriggered", new { notice, branchId = branch?.Id, branchName = branch?.Name, machine = c.MachineName });

        db.AuditLogs.Add(new() { AdminUsername = c.CurrentUser, MachineName = c.MachineName, CommandType = "emergency_trigger", CommandPayload = upperCode + " / " + activeCabinet + " / " + (notes ?? "") + (!string.IsNullOrWhiteSpace(department) ? " / " + department : "") + (branch != null ? " / " + branch.Name : ""), Status = "Completed", Result = "Рассылка передана SignalR; внешний канал: "+delivery.ExternalState });
        await db.SaveChangesAsync();

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
        await EmergencyEvents.AcknowledgeAsync(callId,accepted,reason,c,db,settings,httpFactory,Context.ConnectionAborted);
    }

    public async Task CancelEmergencyAlert()
    {
        try {await EmergencyEvents.CancelAsync(Context.User!,db,settings,Clients,httpFactory,Context.ConnectionAborted);}
        catch(Exception ex) when(ex is UnauthorizedAccessException or ArgumentException) {throw new HubException(ex.Message);}
    }
}
