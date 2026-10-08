using LitleHelperServer;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.RateLimiting;

public static class MachineEnrollmentTests
{
    private sealed class Kerberos : IMessengerKerberos
    {
        public KerberosIdentity Authenticate(string token) => token switch
        {
            "machine" => new("PC1$@ad.test", "mutual"),
            "user" => new("alice@ad.test", null),
            _ => throw new System.Security.Authentication.AuthenticationException()
        };
    }
    private sealed class Directory : IAgentMachineDirectory
    {
        public string ObjectId = new string('a',32);
        public Task<(string Machine,string ObjectId)> VerifyAsync(string principal,CancellationToken token) =>
            principal=="PC1$@ad.test"?Task.FromResult(("PC1",ObjectId)):throw new UnauthorizedAccessException();
    }
    private static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);}
    public static async Task RunAsync()
    {
        var directory=new Directory();
        var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IMessengerKerberos,Kerberos>();builder.Services.AddSingleton<IAgentMachineDirectory>(directory);builder.Services.AddSingleton<ChatGroupGate>();
        builder.Services.AddRateLimiter(o=>o.AddPolicy("registration",c=>RateLimitPartition.GetNoLimiter("test")));
        // Share an in-memory database across endpoint request scopes.
        using var connection=new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        builder.Services.AddDbContext<HelperDb>(o=>o.UseSqlite(connection));
        await using var app=builder.Build();
        app.Use(async(c,next)=>{if(c.Request.Headers["X-Test-Tls"]=="1")c.Request.Scheme="https";try{await next();}catch(UnauthorizedAccessException){c.Response.StatusCode=403;}});
        app.UseRouting();app.UseRateLimiter();AgentMachineEnrollment.Map(app);
        int computerId;
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<HelperDb>();await db.Database.EnsureCreatedAsync();
            var computer=new Computer{MachineName="PC1",AgentKeyHash=Security.KeyHash(new string('x',64))};db.Computers.Add(computer);await db.SaveChangesAsync();computerId=computer.Id;
        }
        await app.StartAsync();using var http=new HttpClient{BaseAddress=new Uri(app.Urls.Single())};
        Task<HttpResponseMessage> Enroll(string name="PC1",string key="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")=>http.PostAsJsonAsync("api/agents/machine-enroll",new AgentRegistrationRequest(name,key));
        Check((await Enroll()).StatusCode==HttpStatusCode.BadRequest,"machine recovery rejects HTTP");
        http.DefaultRequestHeaders.Add("X-Test-Tls","1");http.DefaultRequestHeaders.Add("X-Remote-User","PC1$@ad.test");
        using(var challenge=await Enroll())Check(challenge.StatusCode==HttpStatusCode.Unauthorized&&challenge.Headers.WwwAuthenticate.Any(h=>h.Scheme=="Negotiate"),"spoofed machine header cannot replace key");
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Negotiate","invalid");
        Check((await Enroll()).StatusCode==HttpStatusCode.Unauthorized,"invalid Kerberos rejected");
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Negotiate","user");
        Check((await Enroll()).StatusCode==HttpStatusCode.Forbidden,"AD user cannot recover a computer key");
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Negotiate","machine");
        Check((await Enroll("PC2")).StatusCode==HttpStatusCode.BadRequest,"machine cannot enroll another computer name");
        Check((await Enroll(key:"bad")).StatusCode==HttpStatusCode.BadRequest,"invalid key rejected");
        using(var enrolled=await Enroll())Check(enrolled.IsSuccessStatusCode&&enrolled.Headers.WwwAuthenticate.Any(h=>h.Parameter=="mutual"),"AD computer recovers legacy key with mutual Kerberos reply");
        Check((await Enroll(key:new string('b',64))).IsSuccessStatusCode,"same AD computer can recover after key loss");
        using(var scope=app.Services.CreateScope())
        {
            var computer=await scope.ServiceProvider.GetRequiredService<HelperDb>().Computers.SingleAsync();
            Check(computer.Id==computerId&&computer.AdMachineObjectId==directory.ObjectId&&computer.AgentKeyHash==Security.KeyHash(new string('b',64)),"recovery preserves computer identity and updates only its authenticated key");
        }
        directory.ObjectId=new string('c',32);
        Check((await Enroll()).StatusCode==HttpStatusCode.Conflict,"replacement AD computer object requires administrator approval");
        using(var scope=app.Services.CreateScope())Check((await scope.ServiceProvider.GetRequiredService<HelperDb>().Computers.SingleAsync()).AgentKeyHash==Security.KeyHash(new string('b',64)),"rejected recovery leaves valid key intact");
        await app.StopAsync();
    }
}
