using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;
namespace LitleHelperServer;
public class EmergencySyncWorker(IntegrationSettings settings,IHttpClientFactory httpFactory,IHubContext<HelperHub> hub,
    ILogger<EmergencySyncWorker> logger,TelegramClient? telegram=null,IServiceScopeFactory? scopes=null):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token) {
        while(!token.IsCancellationRequested) {
            try {await PollAsync(token);}
            catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogWarning("AudioRONGTA synchronization will retry: {Error}",ex.GetType().Name);}
            try{await Task.Delay(TimeSpan.FromSeconds(3),token);}catch(OperationCanceledException){break;}
        }
    }
    private async Task PollAsync(CancellationToken token) {
        var options=settings.Emergency();
        if(scopes==null)return;
        using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<HelperDb>();
        await EmergencyEvents.PollTelegramAsync(db,telegram,token);
        if(!options.Enabled || string.IsNullOrWhiteSpace(options.ServerUrl))return;
        if(EmergencyDirectorySync.Due() && !string.IsNullOrWhiteSpace(options.ApiKey)) {
            try {await EmergencyDirectorySync.SendAsync(db,settings,httpFactory,token,false);}
            catch(Exception ex) when(ex is not OperationCanceledException) {EmergencyDirectorySync.Retry();logger.LogWarning("Directory sync will retry: {Error}",ex.GetType().Name);}
        }
        await EmergencyEvents.PollAsync(db,settings,httpFactory,token);
        if(string.IsNullOrWhiteSpace(options.ApiKey))throw new InvalidOperationException("Не настроен межсервисный ключ.");
        using var http=httpFactory.CreateClient("EmergencyBridge");http.Timeout=TimeSpan.FromSeconds(5);
        using var request=new HttpRequestMessage(HttpMethod.Post,options.ServerUrl.TrimEnd('/')+"/api/clients/heartbeat");
        request.Headers.Add("X-Client-Key",options.ApiKey);request.Headers.Add("X-Auth-Token",options.ApiKey);
        request.Content=JsonContent.Create(new{client_id="little-helper-bridge",auth_token=options.ApiKey,cabinet="Сервер помощника"});
        using var response=await http.SendAsync(request,token);response.EnsureSuccessStatusCode();
        using var document=await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token),cancellationToken:token);
        var root=document.RootElement;
        string? activeAlarm=null,activeCall=null;
        if(root.TryGetProperty("active_broadcast",out var alarm) && alarm.ValueKind==JsonValueKind.Object) {
            string id=alarm.TryGetProperty("alarm_id",out var idValue)?idValue.ToString():"";
            if(id.Length>0 && !(alarm.TryGetProperty("origin",out var origin) && origin.GetString()=="helper")) {
                activeAlarm="audio-alarm:"+id;
                string code=Text(alarm,"code","CODE_RED");
                var notice=new EmergencyAlertNotice(code,Text(alarm,"title","ТРЕВОГА"),Text(alarm,"cabinet",""),null,null,null,null,300,null,null,EmergencyLanguages.ForCode(options,code));
                int? branchId=null;string branchName=Text(alarm,"branch_name","");
                if(branchName.Length>0)branchId=await db.Branches.Where(b=>b.IsActive && b.Name==branchName).Select(b=>(int?)b.Id).SingleOrDefaultAsync(token);
                await ImportAsync(activeAlarm,notice,branchId,branchName,null,db,token);
            }
        }
        if(root.TryGetProperty("active_specialist_call",out var call) && call.ValueKind==JsonValueKind.Object) {
            int external=call.GetProperty("id").GetInt32();
            if(external>0 && !await db.EmergencyEvents.AnyAsync(e=>e.ExternalCallId==external && !e.EventId.StartsWith("audio-call:"),token)) {
                activeCall="audio-call:"+external;
                int externalDepartment=call.GetProperty("department_id").GetInt32();
                int? local=settings.LocalDepartmentId(externalDepartment);
                bool syntheticBlue=local==0 && options.Departments?.Any(d=>d.Id==0)!=true;
                var department=options.Departments?.SingleOrDefault(d=>d.Id==local)
                    ?? (syntheticBlue?new SpecialistDepartmentConfig {Id=0,Name="Реанимация"}:throw new InvalidOperationException("Входящий вызов: не найдено сопоставление отделения."));
                string code=syntheticBlue?"CODE_BLUE":"SPECIALIST_CALL";
                var notice=new EmergencyAlertNotice(code,"Срочный вызов: "+department.Name,Text(call,"cabinet",""),null,null,null,null,180,null,syntheticBlue?null:department.Name,EmergencyLanguages.ForCode(options,code));
                await ImportAsync(activeCall,notice,null,null,external,db,token);
            }
        }
        if(root.TryGetProperty("active_white_alert",out var white) && white.ValueKind==JsonValueKind.Object) {
            string wCab=Text(white,"cabinet","");
            string wBranch=Text(white,"branch","Главный корпус");
            string wTime=Text(white,"time","");
            string wEventId=Text(white,"event_id","emergency-white-heartbeat");
            var currentWhite=GuardWhiteService.GetState();
            if(!currentWhite.Active || currentWhite.Cabinet!=wCab) {
                GuardWhiteService.TriggerAlert(wCab,wBranch,wTime.Length>0?wTime:DateTime.Now.ToString("HH:mm:ss"),wEventId);
                await hub.Clients.Group("GuardWhite").SendAsync("CodeWhiteTriggered",new{cabinet=wCab,branchName=wBranch,time=wTime,eventId=wEventId});
            }
        } else {
            var currentWhite=GuardWhiteService.GetState();
            if(currentWhite.Active && currentWhite.EventId!=null && currentWhite.EventId.StartsWith("emergency-white")) {
                GuardWhiteService.ResetAlert(currentWhite.EventId);
                await hub.Clients.Group("GuardWhite").SendAsync("CodeWhiteReset");
            }
        }
        var finished=await db.EmergencyEvents.Where(e=>e.ExternalState=="Imported" && !e.Canceled &&
            ((e.EventId.StartsWith("audio-alarm:") && e.EventId!=activeAlarm) || (e.EventId.StartsWith("audio-call:") && e.EventId!=activeCall))).ToListAsync(token);
        foreach(var entry in finished) {
            if(JsonSerializer.Deserialize<EmergencyAlertNotice>(entry.NoticeJson)?.Code=="CODE_WHITE")GuardWhiteService.ResetAlert(entry.EventId);
            await hub.Clients.Group(entry.BranchId.HasValue?"BranchAgents:"+entry.BranchId:"AllAgents").SendAsync("EmergencyEventCanceled",entry.EventId,token);
            entry.Canceled=true;
        }
        await db.SaveChangesAsync(token);
    }
    private async Task ImportAsync(string id,EmergencyAlertNotice notice,int? branchId,string? branchName,int? externalCall,HelperDb db,CancellationToken token) {
        var existing=await db.EmergencyEvents.SingleOrDefaultAsync(e=>e.EventId==id,token);
        if(existing?.LocalDelivered==true)return;
        if(existing!=null) {
            await EmergencyRecipients.Deliver(JsonSerializer.Deserialize<EmergencyAlertNotice>(existing.NoticeJson)!,branchId,branchName,db,settings,hub.Clients,token);
            existing.LocalDelivered=true;existing.ExternalState="Imported";await db.SaveChangesAsync(token);return;
        }
        await EmergencyEvents.DispatchAsync(notice,branchId,branchName,"AudioRONGTA","Bridge",db,settings,hub.Clients,token,true,externalCall,id);
        logger.LogInformation("Delivered incoming emergency event {Event}",id);
    }
    private static string Text(JsonElement value,string name,string fallback)=>value.TryGetProperty(name,out var field)&&field.ValueKind==JsonValueKind.String?field.GetString()??fallback:fallback;
}
