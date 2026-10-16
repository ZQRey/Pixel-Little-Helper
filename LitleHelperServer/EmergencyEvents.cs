using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;
namespace LitleHelperServer;

// Persist each external operation independently of UI acknowledgement.
public sealed class EmergencyEvent
{
    public int Id { get; set; }
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");
    public string NoticeJson { get; set; } = "";
    public string RecipientMachinesJson { get; set; } = "[]";
    public int? BranchId { get; set; }
    public string BranchName { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Machine { get; set; } = "";
    public bool LocalDelivered { get; set; }
    public string ExternalState { get; set; } = "Disabled";
    public int? ExternalCallId { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public string LastError { get; set; } = "";
    public bool Canceled { get; set; }
    public string TelegramState {get;set;}="Disabled";
    public string TelegramError {get;set;}="";
}
public static class EmergencyEvents
{
    public static async Task EnsureAsync(HelperDb db)
    {
        string id=db.Database.IsNpgsql()?"SERIAL PRIMARY KEY":"INTEGER PRIMARY KEY AUTOINCREMENT";
        string boolean=db.Database.IsNpgsql()?"BOOLEAN":"INTEGER", date=db.Database.IsNpgsql()?"timestamp with time zone":"TEXT";
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS \"EmergencyEvents\" (\"Id\" "+id+
            ",\"EventId\" TEXT NOT NULL,\"NoticeJson\" TEXT NOT NULL,\"RecipientMachinesJson\" TEXT NOT NULL,\"BranchId\" INTEGER NULL,"+
            "\"BranchName\" TEXT NOT NULL,\"Actor\" TEXT NOT NULL,\"Machine\" TEXT NOT NULL,\"LocalDelivered\" "+boolean+" NOT NULL,"+
            "\"ExternalState\" TEXT NOT NULL,\"ExternalCallId\" INTEGER NULL,\"Attempts\" INTEGER NOT NULL,\"CreatedAt\" "+date+
            " NOT NULL,\"NextAttemptAt\" "+date+" NOT NULL,\"LastError\" TEXT NOT NULL,\"Canceled\" "+boolean+" NOT NULL,\"TelegramState\" TEXT NOT NULL,\"TelegramError\" TEXT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_EmergencyEvents_EventId\" ON \"EmergencyEvents\" (\"EventId\")");
    }
    public static async Task<EmergencyEvent> DispatchAsync(EmergencyAlertNotice notice, int? branchId, string? branchName,
        string actor, string machine, HelperDb db, IntegrationSettings settings, IHubClients<IClientProxy> clients, CancellationToken token, bool incoming=false, int? externalCallId=null, string? eventId=null)
    {
        var options=settings.Emergency();
        EmergencyRecipients.ValidateScope(notice,branchId,options);
        var targets=await EmergencyRecipients.TargetMachines(notice,branchId,db,settings,token);
        var entry=new EmergencyEvent {EventId=eventId??Guid.NewGuid().ToString("N"),ExternalCallId=externalCallId,BranchId=branchId,BranchName=branchName??"",Actor=actor,Machine=machine,
            ExternalState=incoming?"Imported":options.Enabled?"Pending":"Disabled",
            TelegramState=settings.Telegram().Enabled && options.TelegramAlertsEnabled?"Pending":"Disabled",
            RecipientMachinesJson=JsonSerializer.Serialize(targets.Select(c=>c.MachineName).ToArray())};
        db.EmergencyEvents.Add(entry); await db.SaveChangesAsync(token);
        notice=notice with { EventId=entry.EventId, CallId=notice.Code=="SPECIALIST_CALL" || externalCallId.HasValue?-entry.Id:notice.CallId };
        entry.NoticeJson=JsonSerializer.Serialize(notice); await db.SaveChangesAsync(token);
        try {
            await EmergencyRecipients.Deliver(notice,branchId,branchName,db,settings,clients,token);
            entry.LocalDelivered=true; await db.SaveChangesAsync(token);
        } catch {
            entry.ExternalState="Blocked"; entry.LastError="Локальная доставка не подтверждена.";
            await db.SaveChangesAsync(CancellationToken.None); throw;
        }
        return entry;
    }
    public static async Task PollTelegramAsync(HelperDb db,TelegramClient? telegram,CancellationToken token)
    {
        if(telegram==null)return;
        var entries=await db.EmergencyEvents.Where(e=>e.TelegramState=="Pending" && e.LocalDelivered).OrderBy(e=>e.Id).Take(10).ToListAsync(token);
        foreach(var entry in entries) {
            if(entry.Canceled || DateTime.UtcNow-entry.CreatedAt>TimeSpan.FromMinutes(5)) {entry.TelegramState="Expired";await db.SaveChangesAsync(token);continue;}
            // Persist before network I/O: Telegram has no idempotency key. A crash or
            // ambiguous timeout requires inspection rather than duplicate alarm delivery.
            entry.TelegramState="Unconfirmed";await db.SaveChangesAsync(token);
            try {
                var notice=JsonSerializer.Deserialize<EmergencyAlertNotice>(entry.NoticeJson)!;
                await telegram.SendEmergencyAlertAsync(notice.Title,notice.Code,notice.Cabinet??"",entry.BranchName,entry.Actor,
                    (notice.Notes??"")+"\\nСобытие: "+entry.EventId,notice.Department,token);
                entry.TelegramState="Sent";
            }catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
            catch(Exception ex){entry.TelegramError=ex is InvalidOperationException?ex.Message:"Telegram не подтвердил доставку; проверьте чат перед повтором.";}
            await db.SaveChangesAsync(token);
        }
    }
    public static async Task PollAsync(HelperDb db, IntegrationSettings settings, IHttpClientFactory factory, CancellationToken token)
    {
        var options=settings.Emergency(); if(!options.Enabled) return;
        var entries=await db.EmergencyEvents.Where(e=>((e.ExternalState=="Pending" && e.LocalDelivered && !e.Canceled) || e.ExternalState=="CancelPending") && e.NextAttemptAt<=DateTime.UtcNow).OrderBy(e=>e.Id).Take(10).ToListAsync(token);
        foreach(var entry in entries) {
            bool cancel=entry.ExternalState=="CancelPending";
            if(!cancel && DateTime.UtcNow-entry.CreatedAt>TimeSpan.FromMinutes(5)) {
                entry.ExternalState="Failed";entry.LastError="Истёк срок внешней доставки; повтор устаревшей тревоги запрещён.";
                await db.SaveChangesAsync(token);continue;
            }
            await db.Entry(entry).ReloadAsync(token);
            if(entry.Canceled && !cancel)continue;
            entry.Attempts++;
            int attempted=entry.Attempts;
            await db.SaveChangesAsync(token);
            try {
                if(!Uri.TryCreate(options.ServerUrl,UriKind.Absolute,out var target) || target.UserInfo.Length>0 ||
                   target.Scheme is not ("http" or "https") ||
                   target.Host is not ("172.16.16.63" or "emergancysystem.gp1.loc" or "localhost" or "127.0.0.1"))
                    throw new InvalidOperationException("Внешняя доставка разрешена только внутреннему серверу Emergency System.");
                if(string.IsNullOrWhiteSpace(options.ApiKey)) throw new InvalidOperationException("Не настроен межсервисный ключ.");
                var notice=JsonSerializer.Deserialize<EmergencyAlertNotice>(entry.NoticeJson)!;
                using var http=factory.CreateClient("EmergencyBridge");http.Timeout=TimeSpan.FromSeconds(10);
                bool specialist=!cancel && notice.Code=="SPECIALIST_CALL";
                int? departmentId=null;
                if(specialist) {
                    var department=options.Departments?.SingleOrDefault(d=>d.Name==notice.Department || d.Id.ToString()==notice.Department)
                        ?? throw new InvalidOperationException("Отделение специалиста не настроено.");
                    departmentId=settings.ExternalDepartmentId(department.Id) ?? throw new InvalidOperationException("Сначала синхронизируйте отделения.");
                }
                using var request=new HttpRequestMessage(HttpMethod.Post,options.ServerUrl.TrimEnd('/')+"/api/integrations/helper/"+(cancel?"cancel":specialist?"specialist":"alert"));
                request.Headers.Add("X-Client-Key",options.ApiKey);
                request.Content=JsonContent.Create(new { event_id=entry.EventId, code=notice.Code,cabinet=notice.Cabinet,notes=notice.Notes,
                    client_name=entry.Actor,image_base64=notice.ImageBase64,image_url=notice.ImageUrl,department=notice.Department,
                    department_id=departmentId,branch_name=entry.BranchName,silent=notice.Code=="CODE_WHITE" });
                using var response=await http.SendAsync(request,token);response.EnsureSuccessStatusCode();
                using var result=await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token),cancellationToken:token);
                int? receivedCallId=specialist?result.RootElement.GetProperty("call_id").GetInt32():entry.ExternalCallId;
                await db.Entry(entry).ReloadAsync(token);
                if(receivedCallId.HasValue)entry.ExternalCallId=receivedCallId;
                // Preserve a cancellation committed while the gateway request was in flight.
                entry.ExternalState=cancel?"Canceled":entry.Canceled?"CancelPending":"Accepted";
                entry.NextAttemptAt=DateTime.UtcNow;entry.LastError="";
            } catch(OperationCanceledException) when(token.IsCancellationRequested) {throw;}
            catch(Exception ex) {
                await db.Entry(entry).ReloadAsync(token);
                if(entry.Canceled && !cancel) {entry.ExternalState="CancelPending";entry.NextAttemptAt=DateTime.UtcNow;await db.SaveChangesAsync(token);continue;}
                entry.Attempts=attempted;
                entry.LastError=ex is InvalidOperationException?ex.Message:"Внешний шлюз не подтвердил приём ("+ex.GetType().Name+").";
                entry.ExternalState=entry.Attempts>=5?"Failed":cancel?"CancelPending":"Pending";
                entry.NextAttemptAt=DateTime.UtcNow.AddSeconds(Math.Min(60,3*Math.Pow(2,entry.Attempts)));
            }
            await db.SaveChangesAsync(token);
        }
    }
    public static async Task CancelAsync(System.Security.Claims.ClaimsPrincipal actor,HelperDb db,IntegrationSettings settings,IHubClients<IClientProxy> clients,IHttpClientFactory? factory,CancellationToken token)
    {
        if(actor.IsInRole("Agent") || !Access.Can(actor,"emergency.manage"))throw new UnauthorizedAccessException("Нет права управления тревогами.");
        var user=await db.Users.SingleAsync(u=>u.Username==actor.Identity!.Name,token);
        if(user.Role!=Roles.SuperAdmin && !user.BranchId.HasValue)throw new ArgumentException("Филиал не определён.");
        var events=await db.EmergencyEvents.Where(e=>!e.Canceled && (user.Role==Roles.SuperAdmin || e.BranchId==user.BranchId)).ToListAsync(token);
        foreach(var entry in events) {
            entry.Canceled=true;
            if(JsonSerializer.Deserialize<EmergencyAlertNotice>(entry.NoticeJson)?.Code=="CODE_WHITE")GuardWhiteService.ResetAlert(entry.EventId);
            await clients.Group(entry.BranchId.HasValue?"BranchAgents:"+entry.BranchId:"AllAgents").SendAsync("EmergencyEventCanceled",entry.EventId,token);
            entry.ExternalState=entry.ExternalState=="Disabled" || (entry.ExternalState=="Pending" && entry.Attempts==0)?"Canceled":"CancelPending";
            entry.Attempts=0;entry.NextAttemptAt=DateTime.UtcNow;
        }
        db.AuditLogs.Add(new() {AdminUsername=user.Username,CommandType="emergency_cancel",Status="Completed",Result="Локальный отбой: "+events.Count+"; внешние отмены поставлены в очередь."});
        await db.SaveChangesAsync(token);
    }

    public static async Task AcknowledgeAsync(int callId,bool accepted,string? reason,Computer computer,HelperDb db,IntegrationSettings settings,IHttpClientFactory? factory,CancellationToken token)
    {
        if(callId>=0) throw new HubException("Этот внешний вызов требует регистрации получателей на сервере.");
        var entry=await db.EmergencyEvents.SingleOrDefaultAsync(e=>e.Id==-callId,token)??throw new HubException("Вызов не найден.");
        if(entry.Canceled || DateTime.UtcNow-entry.CreatedAt>TimeSpan.FromMinutes(10)) throw new HubException("Вызов уже завершён.");
        var machines=JsonSerializer.Deserialize<string[]>(entry.RecipientMachinesJson)??[];
        if(!machines.Contains(computer.MachineName,StringComparer.OrdinalIgnoreCase)) throw new HubException("Компьютер не входит в список получателей вызова.");
        if(accepted && entry.ExternalState!="Disabled") {
            if(entry.ExternalCallId==null || factory==null) throw new HubException("Внешний вызов ещё не зарегистрирован. Повторите подтверждение.");
            var options=settings.Emergency(); using var http=factory.CreateClient("EmergencyBridge");http.Timeout=TimeSpan.FromSeconds(10);
            using var request=new HttpRequestMessage(HttpMethod.Post,options.ServerUrl.TrimEnd('/')+"/api/integrations/helper/calls/"+entry.ExternalCallId+"/ack");
            request.Headers.Add("X-Client-Key",options.ApiKey);
            request.Content=JsonContent.Create(new {acknowledged_by=computer.CurrentUser+" ("+computer.MachineName+")"});
            try { using var response=await http.SendAsync(request,token);response.EnsureSuccessStatusCode(); }
            catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException) {throw new HubException("Шлюз не подтвердил принятие. Повторите попытку.");}
        }
        // A recipient declining must not acknowledge or complete the whole call.
        db.AuditLogs.Add(new() {AdminUsername=computer.CurrentUser,MachineName=computer.MachineName,CommandType="specialist_call_ack",
            CommandPayload=entry.EventId,Status="Completed",Result=accepted?"Принятие подтверждено":"Локальный отказ: "+(reason??"")});
        await db.SaveChangesAsync(token);
    }
}
