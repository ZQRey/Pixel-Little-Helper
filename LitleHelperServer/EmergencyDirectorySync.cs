using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;
namespace LitleHelperServer;
public static class EmergencyDirectorySync
{
    private static int pending=1;
    private static long generation;
    private static readonly ConcurrentDictionary<string,string> fingerprints=new();
    private static string departmentFingerprint="";
    private static long nextAttempt;
    private static readonly SemaphoreSlim gate=new(1,1);
    public static void Request() { Interlocked.Increment(ref generation); Interlocked.Exchange(ref pending,1); Interlocked.Exchange(ref nextAttempt,0); }
    public static bool Due() => Volatile.Read(ref pending)==1 && DateTime.UtcNow.Ticks>=Interlocked.Read(ref nextAttempt);
    public static void Retry() {Interlocked.Exchange(ref pending,1);Interlocked.Exchange(ref nextAttempt,DateTime.UtcNow.AddMinutes(1).Ticks);}
    public static async Task<JsonElement> SendAsync(HelperDb db,IntegrationSettings settings,IHttpClientFactory factory,CancellationToken token,bool full=true)
    {
        var options=settings.Emergency();
        if(!options.Enabled) throw new InvalidOperationException("Включите интеграцию AudioRONGTA.");
        if(string.IsNullOrWhiteSpace(options.ApiKey)) throw new InvalidOperationException("Задайте отдельный межсервисный API-ключ перед синхронизацией.");
        if(!Uri.TryCreate(options.ServerUrl,UriKind.Absolute,out var target) || target.Scheme is not ("http" or "https") ||
            target.Host is not ("emergancysystem.gp1.loc" or "172.16.16.63" or "localhost" or "127.0.0.1") || target.UserInfo.Length>0 ||
            (target.Host is "emergancysystem.gp1.loc" or "172.16.16.63" && target.Port!=8085))
            throw new InvalidOperationException("Синхронизация справочника разрешена только с внутренним сервером Emergency System.");
        long started=Interlocked.Read(ref generation);
        await gate.WaitAsync(token);
        try
        {
            var departments=(options.Departments??[]).ToList();
            var blue=departments.FirstOrDefault(d=>d.Name.Contains("Реаним",StringComparison.OrdinalIgnoreCase)||d.Name.Contains("Синий",StringComparison.OrdinalIgnoreCase));
            if(blue==null){blue=new(){Id=0,Name="Реанимация",SpecialistTitle="Реаниматолог"};departments.Add(blue);}
            var blueUsers=await EmergencyRecipients.ResolveUsers(db,settings,options.CodeBlueResponsibleUsers,options.CodeBlueResponsibleGroups,token);
            var members=new Dictionary<int,HashSet<string>>();
            foreach(var dept in departments)
                members[dept.Id]=await EmergencyRecipients.ResolveUsers(db,settings,dept.ResponsibleUsers,dept.ResponsibleGroups,token);
            foreach(var user in blueUsers) members[blue.Id].Add(user);
            var computers=await db.Computers.AsNoTracking().ToListAsync(token);
            var departmentRows=departments.Select(d=>new{id=d.Id,name=d.Name,color=d.Color,specialistTitle=d.SpecialistTitle}).ToList();
            string currentDepartments=JsonSerializer.Serialize(departmentRows);
            bool sendDepartments=full || currentDepartments!=departmentFingerprint;
            var clientRows=computers.Select(c=>{
                string username=Security.TicketUser(c.CurrentUser).ToLowerInvariant();
                int? departmentId=members[blue.Id].Contains(username)?blue.Id:departments.Where(d=>members[d.Id].Contains(username)).Select(d=>(int?)d.Id).FirstOrDefault();
                string room=c.Room?.Trim()??"";
                if(room.Length>50||room.Equals(c.MachineName,StringComparison.OrdinalIgnoreCase))room="";
                return new{hostname=c.MachineName,username=c.CurrentUser,displayName=c.CurrentUserFullName,cabinet=room,departmentId,ipAddress=c.IpAddress,isOnline=c.IsOnline};
            }).ToList();
            var changes=full?clientRows:clientRows.Where(c=>!fingerprints.TryGetValue(c.hostname,out var old)||old!=JsonSerializer.Serialize(c)).ToList();
            if(changes.Count==0 && !sendDepartments)
            {
                if(Interlocked.Read(ref generation)==started) Interlocked.Exchange(ref pending,0);
                return JsonSerializer.SerializeToElement(new{success=true,clients=0,departments=0});
            }
            var payload=new{departments=sendDepartments?departmentRows:[],clients=changes};
            using var http=factory.CreateClient("EmergencyBridge");http.Timeout=TimeSpan.FromSeconds(30);
            using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(target,"/api/integrations/sync-full"));
            request.Headers.Add("X-Client-Key",options.ApiKey);
            request.Content=JsonContent.Create(payload);
            using var response=await http.SendAsync(request,token);response.EnsureSuccessStatusCode();
            var result=await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken:token);
            if(result.TryGetProperty("departmentMap",out var mapping)) {
                var map=new Dictionary<int,int>();
                foreach(var item in mapping.EnumerateObject()) if(int.TryParse(item.Name,out int id)) map[id]=item.Value.GetInt32();
                settings.SaveDepartmentMap(map);
            }
            foreach(var client in changes) fingerprints[client.hostname]=JsonSerializer.Serialize(client);
            departmentFingerprint=currentDepartments;
            if(Interlocked.Read(ref generation)==started) Interlocked.Exchange(ref pending,0);
            return result;
        }
        finally{gate.Release();}
    }
    public static void MapEmergencyDirectorySync(this WebApplication app)
    {
        app.MapPost("/api/settings/emergency/sync-all",async(HelperDb db,IntegrationSettings settings,IHttpClientFactory factory,CancellationToken token)=>
            Results.Ok(await SendAsync(db,settings,factory,token))).RequireAuthorization("settings.manage");
    }
}
