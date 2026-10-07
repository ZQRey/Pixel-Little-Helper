using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace LitleHelperServer;
public class GlpiImportHealth { public DateTime? LastPollAt {get;set;} public string LastError {get;set;}=""; public long LastId {get;set;} public bool Initialized {get;set;} }
public class GlpiTicketImport(HelperDb db,GlpiService glpi,IntegrationSettings settings,TicketManagementGate gate,GlpiImportHealth health)
{
    internal static string Plain(string html)=>WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html,@"</?(?:p|div|br|li|tr)\b[^>]*>","\n",RegexOptions.IgnoreCase),"<[^>]*>","",RegexOptions.Singleline)).Trim();
    public async Task<int> PollAsync(CancellationToken token)
    {
        if(!settings.GlpiImport().Enabled || !settings.Telegram().Enabled)return 0;
        await gate.Semaphore.WaitAsync(token);
        try
        {
            string key="glpi-import:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(glpi.ImportSource)));
            var cursor=await db.TelegramBotStates.FindAsync([key],token);
            if(cursor==null)
            {
                long last=(await glpi.ImportIdsAsync(null,token)).FirstOrDefault();
                db.TelegramBotStates.Add(new(){Id=key,Offset=last});await db.SaveChangesAsync(token);health.LastId=last;health.Initialized=true;health.LastPollAt=DateTime.UtcNow;health.LastError="";return 0;
            }
            var ids=await glpi.ImportIdsAsync(cursor.Offset,token);var options=settings.GlpiImport();List<GlpiLocation>? locations=null;int imported=0;
            foreach(int id in ids)
            {
                if(!await db.Tickets.AnyAsync(t=>t.GlpiId==id,token))
                {
                    var data=await glpi.GetTicketAsync(id,token);
                    if(!data.TryGetProperty("is_deleted",out var deleted)||GlpiService.Number(deleted)==0)
                    {
                        int status=GlpiService.Number(data.GetProperty("status"));
                        if(status<5)
                        {
                            int location=data.TryGetProperty("locations_id",out var loc)?GlpiService.Number(loc):0;
                            if(location>0)locations??=await glpi.ImportLocationsAsync(token);
                            int? branchId=null;var visited=new HashSet<int>();var leaf=locations?.FirstOrDefault(l=>l.Id==location);int parent=location;
                            while(parent>0&&visited.Add(parent)){if(options.LocationBranches?.TryGetValue(parent,out int b)==true){branchId=b;break;}parent=locations?.FirstOrDefault(l=>l.Id==parent)?.ParentId??0;}
                            var branch=branchId==null?null:await db.Branches.FirstOrDefaultAsync(b=>b.Id==branchId&&b.IsActive,token);
                            var assignees=await glpi.AssigneesAsync(id,token);
                            var ticket=new TicketRecord{GlpiId=id,Username=await glpi.ImportRequesterAsync(id,token),MachineName="Через GLPI",Title=Plain(GlpiService.Text(data,"name")),Description=Plain(GlpiService.Text(data,"content")),BranchId=branch?.Id,BranchName=branch?.Name??"",Room=leaf!=null&&!(options.LocationBranches?.ContainsKey(leaf.Id)??false)?leaf.Name:"",Status=status.ToString(),AssignedGlpiUserId=assignees.FirstOrDefault(),SyncedAt=DateTime.UtcNow};
                            await using var transaction=await db.Database.BeginTransactionAsync(token);
                            db.Tickets.Add(ticket);db.TelegramDeliveries.Add(new(){Ticket=ticket});cursor.Offset=id;await db.SaveChangesAsync(token);await transaction.CommitAsync(token);imported++;
                        }
                    }
                }
                cursor.Offset=id;await db.SaveChangesAsync(token);
            }
            health.Initialized=true;health.LastId=cursor.Offset;health.LastPollAt=DateTime.UtcNow;health.LastError="";return imported;
        } finally {gate.Semaphore.Release();}
    }
}
public class GlpiImportWorker(IServiceScopeFactory scopes,GlpiImportHealth health,ILogger<GlpiImportWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {
            try {using var scope=scopes.CreateScope();await scope.ServiceProvider.GetRequiredService<GlpiTicketImport>().PollAsync(token);}
            catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
            catch(Exception ex){health.LastError=ex is InvalidOperationException?ex.Message:"Не удалось проверить новые заявки GLPI. Проверьте сеть и настройки API.";logger.LogWarning("GLPI ticket import failed: {Type}",ex.GetType().Name);}
            try{await Task.Delay(TimeSpan.FromMinutes(1),token);}catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
        }
    }
}
