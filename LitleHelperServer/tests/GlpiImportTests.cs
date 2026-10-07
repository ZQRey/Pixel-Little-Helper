using LitleHelperServer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text.Json;
public static class GlpiImportTests
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
    private class Transport:HttpMessageHandler
    {
        public readonly SortedDictionary<int,object> Tickets=new();public readonly Dictionary<int,int> Assigned=new();public bool FailRequester;public int Sent,Edited;public string LastBody="";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            string path=request.RequestUri!.AbsolutePath;object result=new {};string body=request.Content==null?"":await request.Content.ReadAsStringAsync(token);LastBody=body;
            if(path.Contains("/bot")){if(path.EndsWith("sendMessage"))Sent++;if(path.EndsWith("editMessageReplyMarkup"))Edited++;return new(HttpStatusCode.OK){Content=JsonContent.Create(new {ok=true,result=new {message_id=999}})};}
            if(path.EndsWith("initSession"))result=new {session_token="test"};
            else if(path.EndsWith("killSession"))result=true;
            else if(path.EndsWith("search/Ticket"))
            {
                string query=Uri.UnescapeDataString(request.RequestUri.Query);long after=query.Contains("[value]=")?long.Parse(query.Split("[value]=")[1].Split('&')[0]):-1;
                var range=System.Text.RegularExpressions.Regex.Match(query,@"range=(\d+)-(\d+)");int start=int.Parse(range.Groups[1].Value),end=int.Parse(range.Groups[2].Value);
                var ids=Tickets.Keys.OrderDescending().Skip(start).Take(end-start+1);
                result=new {data=ids.Select(id=>new Dictionary<string,int>{["2"]=id}).ToArray()};
            }
            else if(path.EndsWith("/Location"))result=new[]{new {id=10,name="Поликлиника",completename="Поликлиника",locations_id=0},new {id=11,name="Кабинет 12",completename="Поликлиника > Кабинет 12",locations_id=10}};
            else if(path.EndsWith("/User/7")){if(FailRequester) return new(HttpStatusCode.ServiceUnavailable){Content=new StringContent("temporary")};result=new {name="alice"};}
            else if(path.EndsWith("/Ticket_User"))result=new object[]{new {type=1,users_id=7}}.Concat(Assigned.GetValueOrDefault(int.Parse(path.Split('/')[3]))>0?new object[]{new {type=2,users_id=Assigned[int.Parse(path.Split('/')[3])]}}:[]).ToArray();
            else if(path.Contains("/Ticket/"))result=Tickets[int.Parse(path.Split('/')[3])];
            else throw new Exception("Unexpected import API: "+path);
            return new(HttpStatusCode.OK){Content=JsonContent.Create(result)};
        }
    }
    public static async Task RunAsync()
    {
        string folder=Path.Combine(Path.GetTempPath(),"helper-import-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Integrations:SettingsFile"]=Path.Combine(folder,"integrations.json"),["Glpi:BaseUrl"]="http://mock/apirest.php",["Glpi:UserToken"]="test",["Glpi:SettingsFile"]=Path.Combine(folder,"glpi.json")}).Build();
            var protector=DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(folder,"keys")));var settings=new IntegrationSettings(config,protector);settings.SaveTelegram(new(true,"123:token","-100123"));settings.SaveGlpiImport(new(true,new(){[10]=1}));
            var transport=new Transport();using var http=new HttpClient(transport);var glpi=new GlpiService(http,new GlpiSettingsStore(config,protector),NullLogger<GlpiService>.Instance);
            var options=new DbContextOptionsBuilder<HelperDb>().UseSqlite("Pooling=False;Data Source="+Path.Combine(folder,"test.db")).Options;await using var db=new HelperDb(options);await db.Database.EnsureCreatedAsync();await TicketManagement.EnsureSchemaAsync(db);db.Branches.Add(new(){Id=1,Name="Поликлиника"});await db.SaveChangesAsync();
            object Ticket(int id,int location=11,int status=1)=>new {id,name="<b>Проблема</b>",content="<p>Текст &amp; описание</p>",locations_id=location,is_deleted=0,status};
            transport.Tickets[100]=Ticket(100);var health=new GlpiImportHealth();var gate=new TicketManagementGate();var importer=new GlpiTicketImport(db,glpi,settings,gate,health);
            Check(await importer.PollAsync(default)==0&&!await db.Tickets.AnyAsync(),"first activation sets cursor without flooding historical GLPI tickets");
            transport.Tickets[101]=Ticket(101);Check(await importer.PollAsync(default)==1,"direct GLPI ticket enters persistent notification queue");var ticket=await db.Tickets.SingleAsync();
            Check(ticket.GlpiId==101&&ticket.Username=="alice"&&ticket.BranchId==1&&ticket.Room=="Кабинет 12"&&ticket.Description=="Текст & описание","import resolves requester ancestor branch room and plain HTML description");
            Check(await importer.PollAsync(default)==0&&await db.TelegramDeliveries.CountAsync()==1,"repeated polling does not duplicate ticket or Telegram notification");
            transport.Tickets[102]=Ticket(102);db.Tickets.Add(new(){GlpiId=102,Username="helper"});await db.SaveChangesAsync();Check(await importer.PollAsync(default)==0,"tickets already created by helper are not imported again");
            transport.Tickets[103]=Ticket(103,0);transport.FailRequester=true;try{await importer.PollAsync(default);throw new Exception("Expected GLPI failure");}catch(InvalidOperationException){}Check(health.LastId==102,"failed import does not advance persisted cursor");transport.FailRequester=false;
            Check(await new GlpiTicketImport(db,glpi,settings,gate,new()).PollAsync(default)==1,"restart retries failed ticket using database cursor");
            Check((await db.Tickets.SingleAsync(t=>t.GlpiId==103)).BranchId==null,"unknown location keeps ticket visible without invented branch");
            transport.Tickets[104]=Ticket(104,status:6);Check(await importer.PollAsync(default)==0,"already closed new GLPI ticket does not generate acceptance notice");
            settings.SaveGlpiImport(new(false));transport.Tickets[105]=Ticket(105);Check(await importer.PollAsync(default)==0,"disabled import does not fetch or enqueue new tickets");
            for(int id=106;id<=215;id++)transport.Tickets[id]=Ticket(id);
            settings.SaveGlpiImport(new(true,new(){[10]=1}));
            Check(await importer.PollAsync(default)==50&&health.LastId==154&&await importer.PollAsync(default)==50&&await importer.PollAsync(default)==11&&health.LastId==215,"pagination imports oldest new IDs first without losing backlog beyond 100 tickets");
            var telegram=new TelegramClient(http,settings,config);Check(await telegram.SendTicketTrackedAsync(ticket,default)==999&&transport.Sent==1,"Telegram message identifier is captured for assignment synchronization");
            settings.SaveTelegram(new(true,null,"-100123",ManagementEnabled:true,DirectoryLogin:"reader",DirectoryPassword:"test"));ticket.AssignedGlpiUserId=42;var delivery=await db.TelegramDeliveries.SingleAsync(d=>d.TicketId==ticket.Id);delivery.GroupChatId="-100123";delivery.GroupMessageId=999;delivery.ButtonState="claim";
            await telegram.SyncTicketButtonAsync(ticket,delivery,default);Check(transport.Edited==1&&transport.LastBody.Contains("claimed"),"assignment in GLPI disables group acceptance button");await telegram.SyncTicketButtonAsync(ticket,delivery,default);Check(transport.Edited==1,"unchanged assignment does not repeatedly edit Telegram notice");
            Console.WriteLine("ALL GLPI IMPORT CHECKS PASSED");
        }finally{Directory.Delete(folder,true);}
    }
}
