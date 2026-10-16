using System.Text.Json;
using LitleHelperServer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;

int checks=0;
void Check(bool value,string name) { if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);checks++; }
async Task Reject(Func<Task> call,string name) { try { await call();throw new Exception("Accepted "+name); } catch(Exception e) when(e is ArgumentException or UnauthorizedAccessException) { Check(true,name); } }
if(Environment.GetEnvironmentVariable("SURVEYS_TEST_PG") is string pgConnection) {
    using var pg=new HelperDb(new DbContextOptionsBuilder<HelperDb>().UseNpgsql(pgConnection).Options);
    await pg.Database.EnsureCreatedAsync();pg.GlpiReportStates.Add(new(){Id="fixture"});await pg.SaveChangesAsync();
    await pg.Database.ExecuteSqlRawAsync("ALTER TABLE \"GlpiReportStates\" DROP COLUMN \"SurveyPolicyJson\"");
    await pg.Database.ExecuteSqlRawAsync("ALTER TABLE \"TicketSurveyInvitations\" DROP COLUMN \"LastShownAt\"");
    await GlpiReports.EnsureSchema(pg);await GlpiReports.EnsureSchema(pg);pg.ChangeTracker.Clear();
    await pg.Database.ExecuteSqlRawAsync("DROP TABLE \"SurveyAdminAlerts\"");
    await SurveyAlerts.EnsureSchema(pg);await SurveyAlerts.EnsureSchema(pg);
    Check((await pg.GlpiReportStates.SingleAsync()).SurveyPolicyJson=="{}","PostgreSQL additive migration preserves rows and JSON default");
    pg.SurveyAdminAlerts.Add(new(){Id="fixture",Source="fixture",UserId=1,UpdatedAt=DateTime.UtcNow,NextNoticeAt=DateTime.UtcNow});await pg.SaveChangesAsync();
    Check(await pg.SurveyAdminAlerts.CountAsync()==1,"PostgreSQL alert ledger migration is repeatable and writable");
    return;
}
var now=DateTime.UtcNow;
var policy=new SurveyPolicy {Mandatory=true,From=now.AddHours(-1)};
var friday=new DateTime(2026,10,16,12,30,0,DateTimeKind.Utc);
Check(policy.AfterBusinessMinutes(friday,120)==new DateTime(2026,10,19,5,30,0,DateTimeKind.Utc),"business reminders skip nights and weekends");
Check(!policy.Applies(new(){ClosedAt=now.AddDays(-1)}),"old closures stay voluntary");
Check(policy.Applies(new(){ClosedAt=now}),"new closures enter mandatory cohort");
Check(!(policy with {BranchIds=[42]}).Applies(new(){ClosedAt=now,BranchId=1}),"pilot branch is enforced");
var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);
var cfg=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Integrations:SettingsFile"]=Path.Combine(path,"integration.json"),["Glpi:SettingsFile"]=Path.Combine(path,"glpi.json"),["GlpiReports:BridgeKeyFile"]=Path.Combine(path,"missing.key") }).Build();
var protection=new EphemeralDataProtectionProvider();
var integrations=new IntegrationSettings(cfg,protection);
var glpi=new GlpiService(new HttpClient(new Unavailable()),new GlpiSettingsStore(cfg,protection),NullLogger<GlpiService>.Instance);
using var db=new HelperDb(new DbContextOptionsBuilder<HelperDb>().UseSqlite("Data Source="+Path.Combine(path,"test.db")).Options);
await db.Database.EnsureCreatedAsync();
await db.Database.ExecuteSqlRawAsync("ALTER TABLE GlpiReportStates DROP COLUMN SurveyPolicyJson");
await db.Database.ExecuteSqlRawAsync("ALTER TABLE TicketSurveyInvitations DROP COLUMN LastSentAt");
await GlpiReports.EnsureSchema(db);await GlpiReports.EnsureSchema(db);
await db.Database.ExecuteSqlRawAsync("DROP TABLE \"SurveyAdminAlerts\"");await SurveyAlerts.EnsureSchema(db);await SurveyAlerts.EnsureSchema(db);
Check(true,"SQLite legacy migration is additive and repeatable");
var gate=new ReportGate();var reports=new GlpiReports(db,glpi,integrations,gate,new ReportHealth());
var services=new ServiceCollection();services.AddLogging();services.AddSignalR();using var provider=services.BuildServiceProvider();
var surveys=new TicketSurveys(db,reports,gate,cfg,provider.GetRequiredService<IHubContext<HelperHub>>());
var state=await reports.State(default);state.SurveysEnabled=true;state.SurveysFrom=now.AddDays(-30);state.SurveyPolicyJson=JsonSerializer.Serialize(policy);
var user=new PanelUser {Username="DOMAIN\\alice",PasswordHash="!AD",IsActive=true};
var ticket=new ReportTicket {Id=901,Status=6,Title="Fixture",ClosedAt=now.AddMinutes(-10),Requesters=[new(7,"alice","Alice")],Responsible=new(8,"tech","Tech")};
state.Payload=JsonSerializer.Serialize(new[]{ticket});await db.SaveChangesAsync();await reports.ReconcileInvitations(state,[ticket],default);
var i=await db.TicketSurveyInvitations.SingleAsync();
Check(i.Mandatory && (await surveys.Pending(user,default)).Length==1,"one mandatory task belongs to verified requester");
await reports.ReconcileInvitations(state,[ticket],default);Check(await db.TicketSurveyInvitations.CountAsync()==1,"sync does not duplicate invitations");
Check((await surveys.Pending(new(){Username="bob",PasswordHash="!AD"},default)).Length==0,"another requester cannot see task");
Check((await surveys.Pending(new(){Username="alice",PasswordHash="local-fixture"},default)).Length==0,"unverified identity cannot see task");
await Reject(()=>surveys.Defer(user,i.Id,true,default),"mandatory survey cannot be dismissed");
Check(await surveys.Claim(user,i.Id,"pc1",default),"first displayed form is acknowledged");
Check(i.FirstShownAt!=null && i.DueAt!=null,"deadline starts at acknowledged display");
var zeroDeferrals=policy with {DailyDeferrals=0};i.NextNoticeAt=now.AddMinutes(-1);
Check(zeroDeferrals.Required(i,now),"zero permitted deferrals requires rating after first display");
Check(!await surveys.Claim(user,i.Id,"pc2",default),"parallel PC cannot claim active form");
await surveys.Defer(user,i.Id,false,default,30);await surveys.Defer(user,i.Id,false,default,120);
await Reject(()=>surveys.Defer(user,i.Id,false,default),"daily deferrals are limited on server");
i.NextNoticeAt=now.AddMinutes(-1);await db.SaveChangesAsync();Check(policy.Required(i,now),"ordinary support action requires answer after deferrals");
await surveys.Claim(user,i.Id,"pc1",default);Check(policy.Required(i,now),"reopening form cannot extend exhausted deferrals");
Check((await surveys.Pending(user,default)).Single().NextNoticeAt>DateTime.UtcNow,"automatic form reminders remain throttled after close");
await Reject(()=>surveys.Answer(new(){Username="bob",PasswordHash="!AD"},i.Id,new(5,"",Guid.NewGuid().ToString()),default),"foreign response rejected");
await Reject(()=>surveys.Answer(user,i.Id,new(0,"",Guid.NewGuid().ToString()),default),"no default or zero rating");
var request=Guid.NewGuid().ToString();await surveys.Answer(user,i.Id,new(2,"Fixture",request),default);
Check(i.State=="Writing" && i.PendingScore==2 && i.PendingRequestId==request,"answer persists before unavailable GLPI write");
await surveys.Answer(user,i.Id,new(2,"Fixture",request),default);Check(i.State=="Writing","same answer request is idempotent");
await Reject(()=>surveys.Answer(user,i.Id,new(5,"Fixture",request),default),"request ID cannot change rating");
db.ChangeTracker.Clear();i=await db.TicketSurveyInvitations.SingleAsync();Check(i.PendingScore==2,"queued answer survives context restart");
var current=await reports.State(default);i.ExpiresAt=now.AddSeconds(-1);await db.SaveChangesAsync();
Check((await surveys.Pending(user,default)).Length==0 && !policy.Required(i,now),"14-day expiry removes actionable requirement");
await Reject(()=>surveys.Defer(user,i.Id,false,default),"expired survey cannot be snoozed");
await Reject(()=>surveys.Answer(user,i.Id,new(5,"",Guid.NewGuid().ToString()),default),"expired survey cannot be answered");
await reports.ReconcileInvitations(current,[ticket],default);Check(i.State=="Expired","queued writes also expire after 14 days");
var result=SurveyReportData.Build([i],[ticket],now);Check(result.Expired==1 && result.Answered==0 && result.ResponsePercent==0,"expired survey remains in report without invented score");
i.State="Pending";i.ExpiresAt=now.AddDays(1);await db.SaveChangesAsync();
await reports.ReconcileInvitations(current,[ticket with {Status=2}],default);Check(i.State=="Cancelled","reopening cancels current obligation");
await reports.ReconcileInvitations(current,[ticket],default);Check(await db.TicketSurveyInvitations.CountAsync()==1,"second closure does not overwrite native single survey");
i.State="Pending";i.Mandatory=true;i.ExpiresAt=now.AddDays(1);await db.SaveChangesAsync();
await reports.ReconcileInvitations(current,[ticket with { Score=4,AnsweredAt=now }],default);
Check(i.State=="Answered","answer made directly in GLPI clears obligation");
current.Payload=JsonSerializer.Serialize(new[]{ticket with {CreatedAt=now.AddDays(-1)}});i.State="Pending";i.LeaseOwner="";i.LeaseUntil=null;await db.SaveChangesAsync();
var admin=new PanelUser {Username="fixture-admin",Role=Roles.SuperAdmin};
await Reject(()=>surveys.Exempt(user,i.Id,"Fixture",default),"ordinary user cannot exempt own obligation");
await surveys.Exempt(admin,i.Id,"Fixture: requester unavailable",default);
Check(i.State=="Exempt" && i.LastError.Contains("unavailable"),"administrator exemption preserves reason without a score");
var filter=new ReportFilter(DateOnly.FromDateTime(now.AddDays(-2)),DateOnly.FromDateTime(now.AddDays(2)),"created",null,null,null,null,"",null);
var data=JsonSerializer.SerializeToElement(await reports.Report(admin,filter,default),new JsonSerializerOptions(JsonSerializerDefaults.Web));
Check(data.GetProperty("surveyReport").GetProperty("exempt").GetInt32()==1,"report includes exemption with matching period");
var xlsx=GlpiReportExport.Xlsx(data,[ticket],filter);
using(var zip=new ZipArchive(new MemoryStream(xlsx))) {
    using var sheet=new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
    Check((await sheet.ReadToEndAsync()).Contains("Обязательные оценки"),"XLSX includes mandatory rating control table");
}
var pdf=GlpiReportExport.Pdf(data,[ticket],filter);
Check(Encoding.ASCII.GetString(pdf,0,4)=="%PDF" && pdf.Length>2000,"PDF export with survey control renders successfully");
var scoped=JsonSerializer.SerializeToElement(await reports.Report(admin,filter,default,[999]),new JsonSerializerOptions(JsonSerializerDefaults.Web));
Check(scoped.GetProperty("surveyReport").GetProperty("rows").GetArrayLength()==0,"survey report respects GLPI entity scope");
await SurveyAlertChecks.Run(db,reports,gate,provider.GetRequiredService<IHubContext<HelperHub>>(),Check,Reject);
await SurveyLegacyChecks.Run(db,reports,surveys,Check);
// Exercise the real signed endpoint over HTTP without accessing production data.
i.State="Pending";await db.SaveChangesAsync();
const string key="0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
File.WriteAllText(cfg["GlpiReports:BridgeKeyFile"]!,key);
var builder=WebApplication.CreateBuilder();builder.WebHost.UseUrls("http://127.0.0.1:0");builder.Configuration.AddConfiguration(cfg);
builder.Services.AddSingleton(db);builder.Services.AddSingleton(reports);builder.Services.AddSingleton(surveys);
builder.Services.AddRateLimiter(options=>options.AddFixedWindowLimiter("glpi-portal",o=>{o.PermitLimit=100;o.Window=TimeSpan.FromSeconds(1);}));
await using(var app=builder.Build()) {
    app.UseRateLimiter();app.MapGlpiReportPortal();await app.StartAsync();
    using var http=new HttpClient {BaseAddress=new Uri(app.Urls.Single())};
    async Task<System.Net.HttpStatusCode> Signed(string operation,int uid=7,string? nonce=null,bool bad=false) {
        string stamp=DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();nonce??=Guid.NewGuid().ToString("N");
        string raw=JsonSerializer.Serialize(new {operation,query=JsonSerializer.Serialize(new {id=i.Id}),userId=uid,username="alice",entities=new[]{0}});
        string signature=Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key),Encoding.UTF8.GetBytes("GLPI-REPORTS\n"+stamp+"\n"+nonce+"\n"+raw))).ToLowerInvariant();
        using var req=new HttpRequestMessage(HttpMethod.Post,"/api/glpi-report-portal"){Content=new StringContent(raw,Encoding.UTF8,"application/json")};
        req.Headers.Add("X-Helper-Time",stamp);req.Headers.Add("X-Helper-Nonce",nonce);req.Headers.Add("X-Helper-Signature",bad?new string('0',64):signature);
        using var response=await http.SendAsync(req);return response.StatusCode;
    }
    Check(await Signed("survey-list")==System.Net.HttpStatusCode.OK,"authenticated GLPI session can list own surveys");
    Check(await Signed("survey-claim",999)==System.Net.HttpStatusCode.Forbidden,"GLPI immutable requester ID prevents impersonation");
    Check(await Signed("survey-list",bad:true)==System.Net.HttpStatusCode.Unauthorized,"forged GLPI signature rejected");
    var nonce=Guid.NewGuid().ToString("N");await Signed("survey-list",nonce:nonce);
    Check(await Signed("survey-list",nonce:nonce)==System.Net.HttpStatusCode.Conflict,"signed request replay rejected");
    await app.StopAsync();
}
Console.WriteLine($"{checks} checks passed");
await db.DisposeAsync();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
Directory.Delete(path,true);
sealed class Unavailable:HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>throw new HttpRequestException("Fixture offline"); }
