using LitleHelperServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

public static class MessengerTests
{
    private class Kerberos : IMessengerKerberos
    {
        public KerberosIdentity Authenticate(string token) => token is "verified-alice" or "verified-bob"
            ? new(token[9..] + "@ad.test", null) : throw new System.Security.Authentication.AuthenticationException();
    }
    private class WindowsDirectory : IMessengerWindowsDirectory
    {
        public Task<AdIdentity> FindAsync(string principal, CancellationToken token) => new Ad().AuthenticateAsync(new LoginRequest(principal.Split('@')[0], "valid"), token);
    }
    private static void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS " + text); }
    private class Ad : IAdAuthentication
    {
        public Task TestConnectionAsync(CancellationToken token) => Task.CompletedTask;
        public Task<AdIdentity> AuthenticateAsync(LoginRequest request, CancellationToken token) => request.Password == "valid" && new[] { "alice", "bob", "charlie" }.Contains(request.Username)
            ? Task.FromResult(new AdIdentity(request.Username + "@ad.test", request.Username == "alice" ? "Алиса" : request.Username == "bob" ? "Борис" : "Чарли", request.Username, new string(request.Username[0], 32))) : throw new UnauthorizedAccessException();
    }
    public static async Task RunAsync()
    {
        string folder = Path.Combine(Path.GetTempPath(), "helper-chat-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:SigningKey"] = new string('k', 48), ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test", ["Messenger:SettingsFile"] = Path.Combine(folder, "messenger.json") });
        string? postgres = Environment.GetEnvironmentVariable("HELPER_CHAT_TEST_POSTGRES");
        builder.Services.AddDbContext<HelperDb>(o => { if (string.IsNullOrEmpty(postgres)) o.UseSqlite("Data Source=" + Path.Combine(folder, "chat.db")); else o.UseNpgsql(postgres); });
        builder.Services.AddSingleton<IAdAuthentication, Ad>(); builder.Services.AddSingleton<PanelSessions>(); builder.Services.AddSingleton<ChatPresence>(); builder.Services.AddSingleton<MessengerSettings>(); builder.Services.AddSignalR();
        builder.Services.AddSingleton<IMessengerKerberos, Kerberos>(); builder.Services.AddSingleton<IMessengerWindowsDirectory, WindowsDirectory>();
        builder.Services.AddSingleton<ChatGroupGate>();
        builder.Services.AddRateLimiter(o => o.AddPolicy("login", c => RateLimitPartition.GetNoLimiter("test")));
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.TokenValidationParameters = new() { ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 48))), ValidateIssuer = true, ValidIssuer = "test", ValidateAudience = true, ValidAudience = "test", ValidateLifetime = true };
            o.Events = new JwtBearerEvents { OnMessageReceived = c => { if (c.Request.Path.StartsWithSegments("/messengerHub")) c.Token = c.Request.Query["access_token"]; return Task.CompletedTask; } };
        });
        builder.Services.AddAuthorization(o => { o.AddPolicy("Panel", p => p.RequireAuthenticatedUser().RequireRole(Roles.All)); o.AddPolicy("settings.manage", p => p.RequireClaim("permission", "settings.manage")); });
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers["X-Forwarded-Proto"] == "https") context.Request.Scheme = "https"; // Test proxy only.
            try { await next(); }
            catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException) { context.Response.StatusCode = ex is UnauthorizedAccessException ? 403 : 400; }
        });
        app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization(); app.MapMessenger();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelperDb>(); await db.Database.EnsureCreatedAsync(); await Messenger.EnsureSchemaAsync(db); await Messenger.EnsureSchemaAsync(db);
            Check(await db.ChatMessages.CountAsync() == 0, "chat schema initialization is idempotent");
            await ChatGroups.EnsureSchemaAsync(db); await ChatGroups.EnsureSchemaAsync(db);
            db.Users.Add(new() { Username = "admin", FullName = "Local admin", Role = Roles.SuperAdmin, PasswordHash = "local" }); await db.SaveChangesAsync();
        }
        await app.StartAsync(); string address = app.Urls.Single();
        using var anonymous = new HttpClient { BaseAddress = new Uri(address) };
        Check((await anonymous.GetAsync("api/messenger/users")).StatusCode == HttpStatusCode.Unauthorized, "anonymous users cannot read messenger directory");
        Check((await anonymous.PostAsJsonAsync("api/messenger/login", new { username = "alice", password = "valid" })).StatusCode == HttpStatusCode.BadRequest, "AD password login requires HTTPS");
        async Task<(HttpClient Http, string Token, int Id)> Login(string name)
        {
            var http = new HttpClient { BaseAddress = new Uri(address) }; http.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
            using var result = await http.PostAsJsonAsync("api/messenger/login", new { username = name, password = "valid" }); result.EnsureSuccessStatusCode();
            var json = await result.Content.ReadFromJsonAsync<JsonElement>(); string token = json.GetProperty("token").GetString()!;
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); return (http, token, json.GetProperty("id").GetInt32());
        }
        var alice = await Login("alice"); var bob = await Login("bob"); var charlie = await Login("charlie");
        Check((await anonymous.GetAsync("api/messenger/windows")).StatusCode == HttpStatusCode.BadRequest, "Windows SSO requires HTTPS");
        using(var browser=new HttpClient(new HttpClientHandler { UseCookies=false }) { BaseAddress=new Uri(address) })
        {
            browser.DefaultRequestHeaders.Add("X-Forwarded-Proto","https");browser.DefaultRequestHeaders.Add("X-Messenger-Web","1");
            using var webLogin=await browser.PostAsJsonAsync("api/messenger/web/login",new LoginRequest("alice","valid"));webLogin.EnsureSuccessStatusCode();
            string cookie=webLogin.Headers.GetValues("Set-Cookie").Single();
            Check(cookie.Contains("httponly",StringComparison.OrdinalIgnoreCase)&&cookie.Contains("secure",StringComparison.OrdinalIgnoreCase)&&cookie.Contains("samesite=strict",StringComparison.OrdinalIgnoreCase),"web session is Secure, HttpOnly and SameSite Strict");
            browser.DefaultRequestHeaders.Add("Cookie",cookie.Split(';')[0]);
            Check((await browser.GetAsync("api/messenger/me")).IsSuccessStatusCode,"web cookie opens same AD messenger account");
            browser.DefaultRequestHeaders.Remove("X-Messenger-Web");
            Check(!(await browser.PostAsJsonAsync("api/messenger/read/999/1",new {})).IsSuccessStatusCode,"web mutation without CSRF header rejected");
            browser.DefaultRequestHeaders.Add("X-Messenger-Web","1");browser.DefaultRequestHeaders.Add("Origin","https://evil.example");
            Check(!(await browser.PostAsJsonAsync("api/messenger/read/999/1",new {})).IsSuccessStatusCode,"cross-origin web mutation rejected");
            browser.DefaultRequestHeaders.Remove("Origin");browser.DefaultRequestHeaders.Remove("X-Forwarded-Proto");
            Check(!(await browser.GetAsync("api/messenger/me")).IsSuccessStatusCode,"web session cannot travel over HTTP");
        }
        using var windows = new HttpClient { BaseAddress = new Uri(address) }; windows.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        windows.DefaultRequestHeaders.Add("X-Remote-User", "alice@ad.test");
        using (var challenge = await windows.GetAsync("api/messenger/windows"))
            Check(challenge.StatusCode == HttpStatusCode.Unauthorized && challenge.Headers.WwwAuthenticate.Any(h => h.Scheme == "Negotiate"), "SSO challenges and ignores spoofed identity headers");
        windows.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Negotiate", "fake");
        Check((await windows.GetAsync("api/messenger/windows")).StatusCode == HttpStatusCode.Unauthorized, "unverified Kerberos token cannot log in");
        foreach (var actor in new[] { (Name: "alice", Id: alice.Id), (Name: "bob", Id: bob.Id) })
        {
            windows.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Negotiate", "verified-" + actor.Name);
            using var response = await windows.GetAsync("api/messenger/windows"); response.EnsureSuccessStatusCode();
            var data = await response.Content.ReadFromJsonAsync<JsonElement>();
            Check(data.GetProperty("id").GetInt32() == actor.Id, "Windows SSO preserves separate identity for " + actor.Name);
            var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(data.GetProperty("token").GetString());
            Check(jwt.Claims.Any(c => c.Type == "messenger" && c.Value == "1"), "Windows SSO token is restricted to messenger");
        }
        using var a = alice.Http; using var b = bob.Http; using var c = charlie.Http;
        Check((await a.GetAsync("api/settings/messenger")).StatusCode == HttpStatusCode.Forbidden, "messenger session cannot manage server settings");
        var users = await a.GetFromJsonAsync<JsonElement>("api/messenger/users");
        Check(users.GetArrayLength() == 2 && users.EnumerateArray().Any(u => u.GetProperty("fullName").GetString() == "Борис"), "directory uses AD display names and excludes self and local accounts");
        var received = new TaskCompletionSource<ChatMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var hub = new HubConnectionBuilder().WithUrl(address + "/messengerHub", o => o.AccessTokenProvider = () => Task.FromResult<string?>(bob.Token)).Build();
        hub.On<ChatMessage>("ChatMessage", m => received.TrySetResult(m)); await hub.StartAsync();
        var request = new ChatSend(bob.Id, "Привет, Борис! :helper_wave: :helper_joy: 😄", Guid.NewGuid().ToString());
        using var sent = await a.PostAsJsonAsync("api/messenger/send", request); sent.EnsureSuccessStatusCode(); var message = await sent.Content.ReadFromJsonAsync<ChatMessage>();
        var delivered = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Check(delivered.Body == request.Body && delivered.SenderId == alice.Id, "real SignalR delivery carries authenticated sender and stored message");
        (await a.PostAsJsonAsync("api/messenger/preferences/"+bob.Id,new PreferenceUpdate(true,true,true))).EnsureSuccessStatusCode();
        Check((await a.GetFromJsonAsync<List<ChatPreference>>("api/messenger/preferences"))!.Single().Pinned,"chat pin, favourite and mute persist");
        Check((await b.GetFromJsonAsync<List<ChatPreference>>("api/messenger/preferences"))!.Count==0,"chat preferences isolated by authenticated user");
        (await b.PostAsJsonAsync($"api/messenger/reaction/{alice.Id}/{message!.Id}",new ReactionUpdate("👍"))).EnsureSuccessStatusCode();
        var reactions=await a.GetFromJsonAsync<Dictionary<string,List<ReactionInfo>>>($"api/messenger/extras/{bob.Id}?ids={message.Id}");
        Check(reactions![message.Id.ToString()].Single().Count==1,"message reaction synchronized between participants");
        Check((await c.PostAsJsonAsync($"api/messenger/reaction/{alice.Id}/{message.Id}",new ReactionUpdate("👍"))).StatusCode==HttpStatusCode.Forbidden,"third user cannot react to private message");
        (await b.PostAsJsonAsync($"api/messenger/reaction/{alice.Id}/{message.Id}",new ReactionUpdate("👍"))).EnsureSuccessStatusCode();
        Check((await a.GetFromJsonAsync<Dictionary<string,List<ReactionInfo>>>($"api/messenger/extras/{bob.Id}?ids={message.Id}"))![message.Id.ToString()].Count==0,"repeated reaction toggles off without duplicate");
        Check((await b.GetFromJsonAsync<JsonElement>($"api/messenger/search/{alice.Id}?q=helper_wave")).GetArrayLength()==1,"server history search finds available message");
        Check((await c.GetFromJsonAsync<JsonElement>($"api/messenger/search/{alice.Id}?q=helper_wave")).GetArrayLength()==0,"history search does not expose other conversations");
        Check((await b.GetFromJsonAsync<JsonElement>("api/messenger/history/" + alice.Id)).GetArrayLength() == 1, "recipient sees persistent dialogue history");
        Check((await b.GetFromJsonAsync<JsonElement>("api/messenger/history/" + alice.Id))[0].GetProperty("body").GetString() == request.Body, "private emoji codes and Unicode survive storage and retrieval");
        Check((await c.GetFromJsonAsync<JsonElement>("api/messenger/history/" + alice.Id)).GetArrayLength() == 0, "third user cannot read another dialogue");
        using var retry = await a.PostAsJsonAsync("api/messenger/send", request); retry.EnsureSuccessStatusCode();
        Check((await retry.Content.ReadFromJsonAsync<ChatMessage>())!.Id == message!.Id, "retry returns original message without duplication");
        using var conflict = await a.PostAsJsonAsync("api/messenger/send", request with { Body = "Other" }); Check(conflict.StatusCode == HttpStatusCode.BadRequest, "reused message identity cannot change content");
        var unread = await b.GetFromJsonAsync<JsonElement>("api/messenger/users"); Check(unread.EnumerateArray().Single(u => u.GetProperty("id").GetInt32() == alice.Id).GetProperty("unread").GetInt32() == 1, "recipient has unread count");
        using var unrelatedRead = await c.PostAsJsonAsync($"api/messenger/read/{alice.Id}/{message.Id}", new { }); unrelatedRead.EnsureSuccessStatusCode();
        Check((await a.GetFromJsonAsync<JsonElement>("api/messenger/history/" + bob.Id))[0].GetProperty("readAt").ValueKind == JsonValueKind.Null, "third user cannot mark another participant's message read");
        using var read = await b.PostAsJsonAsync($"api/messenger/read/{alice.Id}/{message.Id}", new { }); read.EnsureSuccessStatusCode();
        Check((await a.GetFromJsonAsync<JsonElement>("api/messenger/history/" + bob.Id))[0].GetProperty("readAt").ValueKind == JsonValueKind.String, "read receipt visible to sender");
        await hub.StopAsync();
        for (int i = 0; i < 55; i++) { using var response = await a.PostAsJsonAsync("api/messenger/send", new ChatSend(bob.Id, "Message " + i, Guid.NewGuid().ToString())); response.EnsureSuccessStatusCode(); }
        var page = await b.GetFromJsonAsync<JsonElement>("api/messenger/history/" + alice.Id); Check(page.GetArrayLength() == 50, "history is paged while recipient is offline");
        var older = await b.GetFromJsonAsync<JsonElement>($"api/messenger/history/{alice.Id}?before={page[0].GetProperty("id").GetInt64()}"); Check(older.GetArrayLength() == 6, "older history remains accessible");
        using var created = await a.PostAsJsonAsync("api/messenger/groups", new GroupCreate("Рабочая группа", [bob.Id])); created.EnsureSuccessStatusCode();
        int groupPeer = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32(), groupId = -groupPeer;
        Check((await c.GetAsync($"api/messenger/groups/{groupId}")).StatusCode == HttpStatusCode.Forbidden, "non-member cannot inspect a group");
        Check((await c.GetAsync($"api/messenger/history/{groupPeer}")).StatusCode == HttpStatusCode.Forbidden, "non-member cannot read group history");
        Check((await b.PostAsJsonAsync($"api/messenger/groups/{groupId}/invite", new GroupUser(charlie.Id))).StatusCode == HttpStatusCode.Forbidden, "only group creator may invite members");
        var groupReceived = new TaskCompletionSource<ChatMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<ChatMessage>("ChatMessage", m => { if (m.RecipientId == groupPeer) groupReceived.TrySetResult(m); }); await hub.StartAsync();
        var groupRequest = new ChatSend(groupPeer, "Первое сообщение группы :helper_party: :helper_thanks:", Guid.NewGuid().ToString());
        var concurrent = await Task.WhenAll(a.PostAsJsonAsync("api/messenger/send", groupRequest), a.PostAsJsonAsync("api/messenger/send", groupRequest));
        foreach (var response in concurrent) response.EnsureSuccessStatusCode();
        var groupMessage = await concurrent[0].Content.ReadFromJsonAsync<ChatMessage>();
        Check((await concurrent[1].Content.ReadFromJsonAsync<ChatMessage>())!.Id == groupMessage!.Id, "concurrent group retries create one message"); foreach (var response in concurrent) response.Dispose();
        Check((await groupReceived.Task.WaitAsync(TimeSpan.FromSeconds(10))).SenderName == "Алиса", "group messages deliver through SignalR with sender display name");
        Check((await b.GetFromJsonAsync<JsonElement>($"api/messenger/history/{groupPeer}"))[0].GetProperty("body").GetString() == groupRequest.Body, "group emoji codes survive storage and retrieval");
        var groupList = await b.GetFromJsonAsync<JsonElement>("api/messenger/users");
        Check(groupList.EnumerateArray().Single(u => u.GetProperty("id").GetInt32() == groupPeer).GetProperty("unread").GetInt32() == 1, "group unread counter is per member");
        (await b.PostAsJsonAsync($"api/messenger/read/{groupPeer}/{groupMessage.Id}", new { })).EnsureSuccessStatusCode();
        Check((await b.GetFromJsonAsync<JsonElement>("api/messenger/users")).EnumerateArray().Single(u => u.GetProperty("id").GetInt32() == groupPeer).GetProperty("unread").GetInt32() == 0, "group read cursor clears own badge");
        Check((await c.PostAsJsonAsync($"api/messenger/read/{groupPeer}/{groupMessage.Id}", new { })).StatusCode == HttpStatusCode.Forbidden, "third user cannot mark group messages read");
        (await a.PostAsJsonAsync($"api/messenger/groups/{groupId}/invite", new GroupUser(charlie.Id))).EnsureSuccessStatusCode();
        Check((await c.GetFromJsonAsync<JsonElement>($"api/messenger/history/{groupPeer}")).GetArrayLength() == 0, "new group participant cannot read earlier history");
        (await a.PostAsJsonAsync("api/messenger/send", new ChatSend(groupPeer, "После приглашения", Guid.NewGuid().ToString()))).EnsureSuccessStatusCode();
        Check((await c.GetFromJsonAsync<JsonElement>($"api/messenger/history/{groupPeer}")).GetArrayLength() == 1, "new group participant sees messages from joining");
        (await a.PostAsJsonAsync($"api/messenger/groups/{groupId}/remove", new GroupUser(bob.Id))).EnsureSuccessStatusCode();
        Check((await b.GetAsync($"api/messenger/history/{groupPeer}")).StatusCode == HttpStatusCode.Forbidden, "removed participant loses group history access");
        Check((await b.PostAsJsonAsync("api/messenger/send", new ChatSend(groupPeer, "Forbidden", Guid.NewGuid().ToString()))).StatusCode == HttpStatusCode.Forbidden, "removed participant cannot send to group");
        Check(!(await b.GetFromJsonAsync<JsonElement>("api/messenger/users")).EnumerateArray().Any(u => u.GetProperty("id").GetInt32() == groupPeer), "removed group disappears from directory");
        await hub.StopAsync();
        for (int i = 0; i < 55; i++) (await a.PostAsJsonAsync("api/messenger/send", new ChatSend(groupPeer, "Group message " + i, Guid.NewGuid().ToString()))).EnsureSuccessStatusCode();
        var groupPage = await c.GetFromJsonAsync<JsonElement>($"api/messenger/history/{groupPeer}");
        Check(groupPage.GetArrayLength() == 50 && (await c.GetFromJsonAsync<JsonElement>($"api/messenger/history/{groupPeer}?before={groupPage[0].GetProperty("id").GetInt64()}")).GetArrayLength() == 6, "group history pagination respects joining boundary");
        Check((await a.PostAsJsonAsync($"api/messenger/groups/{groupId}/leave", new { })).StatusCode == HttpStatusCode.BadRequest, "creator must transfer ownership before leaving");
        (await a.PostAsJsonAsync($"api/messenger/groups/{groupId}/owner", new GroupUser(charlie.Id))).EnsureSuccessStatusCode();
        Check((await a.PostAsJsonAsync($"api/messenger/groups/{groupId}/invite", new GroupUser(bob.Id))).StatusCode == HttpStatusCode.Forbidden, "former owner cannot invite participants");
        (await c.PostAsJsonAsync($"api/messenger/groups/{groupId}/invite", new GroupUser(bob.Id))).EnsureSuccessStatusCode();
        var returningHistory = await b.GetFromJsonAsync<List<ChatMessage>>($"api/messenger/history/{groupPeer}");
        Check(returningHistory!.Count == 2 && returningHistory.All(m => !m.Body.StartsWith("Group message")), "reinvited member retains previously accessible history and cannot read messages during absence");
        (await c.PostAsJsonAsync($"api/messenger/groups/{groupId}/rename", new GroupUpdate("Новая группа"))).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync($"api/messenger/groups/{groupId}/close", new { })).EnsureSuccessStatusCode();
        Check((await a.PostAsJsonAsync("api/messenger/send", new ChatSend(groupPeer, "Closed", Guid.NewGuid().ToString()))).StatusCode == HttpStatusCode.BadRequest, "closed group rejects messages");
        Check((await a.GetFromJsonAsync<JsonElement>($"api/messenger/history/{groupPeer}")).GetArrayLength() == 50, "closed group retains readable history");
        (await a.PostAsJsonAsync($"api/messenger/groups/{groupId}/leave", new { })).EnsureSuccessStatusCode();
        Check((await a.GetAsync($"api/messenger/history/{groupPeer}")).StatusCode == HttpStatusCode.Forbidden, "leaving closed group revokes access");
        using var invalid = await a.PostAsJsonAsync("api/messenger/send", new ChatSend(bob.Id, new string('x', 4001), Guid.NewGuid().ToString())); Check(invalid.StatusCode == HttpStatusCode.BadRequest, "oversized messages rejected");
        async Task<HttpResponseMessage> Upload(HttpClient actor, int peer, string key, string filename = "../Документ.exe", byte[]? data = null)
        {
            using var form = new MultipartFormDataContent(); form.Add(new StringContent(peer.ToString()), "peer"); form.Add(new StringContent(key), "clientId"); form.Add(new StringContent(""), "body");
            form.Add(new ByteArrayContent(data ?? [0, 1, 2, 255]), "files", filename); return await actor.PostAsync("api/messenger/files/send", form);
        }
        string uploadKey = Guid.NewGuid().ToString("N");
        using var uploaded = await Upload(a, charlie.Id, uploadKey); uploaded.EnsureSuccessStatusCode(); var fileMessage = (await uploaded.Content.ReadFromJsonAsync<ChatMessage>())!; var personalFile = fileMessage.Attachments.Single();
        Check(personalFile.Name == "Документ.exe" && fileMessage.Body.Contains("Вложения:"), "all extensions accepted and paths stripped from filenames");
        Check((await c.GetByteArrayAsync("api/messenger/files/" + personalFile.Id)).SequenceEqual(new byte[] { 0, 1, 2, 255 }), "recipient downloads exact original bytes");
        using (var download = await a.GetAsync("api/messenger/files/" + personalFile.Id)) Check(download.IsSuccessStatusCode && download.Content.Headers.ContentDisposition?.DispositionType == "attachment" && download.Headers.GetValues("X-Content-Type-Options").Single() == "nosniff", "sender download uses attachment disposition and nosniff");
        Check((await b.GetAsync("api/messenger/files/" + personalFile.Id)).StatusCode == HttpStatusCode.NotFound && (await anonymous.GetAsync("api/messenger/files/" + personalFile.Id)).StatusCode == HttpStatusCode.Unauthorized, "outsider and anonymous cannot download private files");
        Check((await c.GetFromJsonAsync<List<ChatMessage>>("api/messenger/history/" + alice.Id))!.Single().Attachments.Single().Id == personalFile.Id, "attachment metadata persists in history");
        using var repeatUpload = await Upload(a, charlie.Id, uploadKey); repeatUpload.EnsureSuccessStatusCode();
        Check((await repeatUpload.Content.ReadFromJsonAsync<ChatMessage>())!.Id == fileMessage.Id, "upload retry creates no duplicate message or file");
        Check((await Upload(a, charlie.Id, uploadKey, data: [9])).StatusCode == HttpStatusCode.BadRequest, "upload retry cannot change original bytes");
        Check((await Upload(a, charlie.Id, Guid.NewGuid().ToString(), data: new byte[50 * 1024 * 1024 + 1])).StatusCode == HttpStatusCode.BadRequest, "file over 50 MB is rejected");
        using var fileGroupCreated = await a.PostAsJsonAsync("api/messenger/groups", new GroupCreate("Вложения", [bob.Id])); fileGroupCreated.EnsureSuccessStatusCode(); int filePeer = (await fileGroupCreated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        using var groupUpload = await Upload(a, filePeer, Guid.NewGuid().ToString(), "photo.png"); groupUpload.EnsureSuccessStatusCode(); var groupFileMessage = (await groupUpload.Content.ReadFromJsonAsync<ChatMessage>())!; string groupFile = groupFileMessage.Attachments.Single().Id;
        Check((await b.GetAsync("api/messenger/files/" + groupFile)).IsSuccessStatusCode, "group member can download attachment");
        Check((await b.PostAsJsonAsync($"api/messenger/groups/{-filePeer}/suspend", new GroupSuspend(alice.Id,60))).StatusCode == HttpStatusCode.Forbidden, "only owner can suspend group members");
        (await a.PostAsJsonAsync($"api/messenger/groups/{-filePeer}/suspend", new GroupSuspend(bob.Id,60))).EnsureSuccessStatusCode();
        Check((await b.GetAsync($"api/messenger/history/{filePeer}")).StatusCode == HttpStatusCode.Forbidden && (await b.GetAsync("api/messenger/files/"+groupFile)).StatusCode == HttpStatusCode.NotFound, "suspension blocks group history and attachments");
        Check((await b.PostAsJsonAsync("api/messenger/send",new ChatSend(filePeer,"Blocked",Guid.NewGuid().ToString()))).StatusCode==HttpStatusCode.Forbidden, "suspended member cannot send");
        (await a.PostAsJsonAsync("api/messenger/send",new ChatSend(filePeer,"During suspension",Guid.NewGuid().ToString()))).EnsureSuccessStatusCode();
        (await a.PostAsJsonAsync($"api/messenger/groups/{-filePeer}/resume",new GroupSuspend(bob.Id,0))).EnsureSuccessStatusCode();
        Check((await b.GetFromJsonAsync<List<ChatMessage>>($"api/messenger/history/{filePeer}"))!.Count==1, "resumed member does not receive messages from suspension interval");
        (await a.PostAsJsonAsync($"api/messenger/groups/{-filePeer}/suspend",new GroupSuspend(bob.Id,1))).EnsureSuccessStatusCode();
        using(var expiryScope=app.Services.CreateScope()) { var expiryDb=expiryScope.ServiceProvider.GetRequiredService<HelperDb>(); var expiresAt=DateTime.UtcNow.AddMilliseconds(50); await expiryDb.ChatGroupRestrictions.Where(r=>r.UserId==bob.Id && r.EndsAt>DateTime.UtcNow).ExecuteUpdateAsync(s=>s.SetProperty(r=>r.EndsAt,expiresAt)); }
        await Task.Delay(100); Check((await b.GetAsync($"api/messenger/history/{filePeer}")).IsSuccessStatusCode,"suspension expires automatically without client intervention");
        (await a.PostAsJsonAsync($"api/messenger/groups/{-filePeer}/invite", new GroupUser(charlie.Id))).EnsureSuccessStatusCode();
        Check((await c.GetAsync("api/messenger/files/" + groupFile)).StatusCode == HttpStatusCode.NotFound, "new member cannot download files sent before joining");
        (await a.PostAsJsonAsync($"api/messenger/groups/{-filePeer}/remove", new GroupUser(bob.Id))).EnsureSuccessStatusCode();
        Check((await b.GetAsync("api/messenger/files/" + groupFile)).StatusCode == HttpStatusCode.NotFound, "removed group member loses file access");
        (await a.PostAsJsonAsync($"api/messenger/groups/{-filePeer}/close", new { })).EnsureSuccessStatusCode();
        Check((await Upload(a, filePeer, Guid.NewGuid().ToString())).StatusCode == HttpStatusCode.BadRequest, "closed group rejects attachment uploads");
        using (var cleanupScope = app.Services.CreateScope())
        {
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<HelperDb>(); await cleanupDb.ChatMessages.Where(m => m.Id == fileMessage.Id).ExecuteDeleteAsync();
            await ChatFiles.CleanupAsync(cleanupDb, builder.Configuration, default);
            Check(!File.Exists(Path.Combine(ChatFiles.Folder(builder.Configuration), personalFile.Id)) && !await cleanupDb.ChatFiles.AnyAsync(f => f.Id == personalFile.Id), "retention removes attachment metadata and stored file");
        }
        using var roleGroupResponse = await a.PostAsJsonAsync("api/messenger/groups", new GroupCreate("Роли", [bob.Id,charlie.Id])); roleGroupResponse.EnsureSuccessStatusCode();
        int rolePeer = (await roleGroupResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32(), roleId = -rolePeer;
        async Task<HttpStatusCode> Operation(HttpClient actor,string op,object body) { using var response=await actor.PostAsJsonAsync($"api/messenger/groups/{roleId}/{op}",body); return response.StatusCode; }
        Check(await Operation(b,"admin",new GroupUser(charlie.Id))==HttpStatusCode.Forbidden,"participant cannot appoint group administrators");
        Check(await Operation(a,"admin",new GroupUser(bob.Id))==HttpStatusCode.OK,"creator appoints administrator");
        Check((await b.GetFromJsonAsync<JsonElement>($"api/messenger/groups/{roleId}")).GetProperty("members").EnumerateArray().Single(m=>m.GetProperty("id").GetInt32()==bob.Id).GetProperty("isAdmin").GetBoolean(),"group API exposes administrator role");
        Check(await Operation(b,"rename",new GroupUpdate("Администратор изменил название"))==HttpStatusCode.OK,"group administrator can rename");
        Check(await Operation(b,"remove",new GroupUser(alice.Id))==HttpStatusCode.Forbidden && await Operation(b,"suspend",new GroupSuspend(alice.Id,60))==HttpStatusCode.Forbidden,"administrator cannot remove or suspend creator");
        Check(await Operation(b,"delete",new{})==HttpStatusCode.Forbidden && await Operation(b,"owner",new GroupUser(charlie.Id))==HttpStatusCode.Forbidden && await Operation(b,"admin",new GroupUser(charlie.Id))==HttpStatusCode.Forbidden,"administrator cannot delete transfer ownership or grant roles");
        Check(await Operation(a,"admin",new GroupUser(charlie.Id))==HttpStatusCode.OK,"creator appoints multiple administrators");
        Check(await Operation(b,"remove",new GroupUser(charlie.Id))==HttpStatusCode.Forbidden && await Operation(b,"resume",new GroupSuspend(charlie.Id,0))==HttpStatusCode.Forbidden,"administrator cannot manage another administrator");
        Check(await Operation(a,"unadmin",new GroupUser(charlie.Id))==HttpStatusCode.OK && await Operation(b,"remove",new GroupUser(charlie.Id))==HttpStatusCode.OK,"administrator can exclude ordinary member");
        Check((await c.GetAsync($"api/messenger/groups/{roleId}")).StatusCode==HttpStatusCode.Forbidden,"excluded member cannot read roles or group events");
        Check(await Operation(b,"invite",new GroupUser(charlie.Id))==HttpStatusCode.OK,"administrator can invite member");
        Check(await Operation(a,"unadmin",new GroupUser(bob.Id))==HttpStatusCode.OK && await Operation(b,"rename",new GroupUpdate("Denied"))==HttpStatusCode.Forbidden,"revoked administrator loses management rights immediately");
        Check(await Operation(c,"leave",new{})==HttpStatusCode.OK && (await c.GetAsync($"api/messenger/history/{rolePeer}")).StatusCode==HttpStatusCode.Forbidden,"ordinary member can leave and loses group access");
        using var deletionUpload = await Upload(a,rolePeer,Guid.NewGuid().ToString(),"retained.bin"); deletionUpload.EnsureSuccessStatusCode(); var retainedFile=(await deletionUpload.Content.ReadFromJsonAsync<ChatMessage>())!.Attachments.Single().Id;
        Check(await Operation(a,"delete",new{})==HttpStatusCode.OK,"creator can delete group for everyone");
        Check(!(await b.GetFromJsonAsync<JsonElement>("api/messenger/users")).EnumerateArray().Any(g=>g.GetProperty("id").GetInt32()==rolePeer),"deleted group disappears from all chat lists");
        Check((await a.GetAsync($"api/messenger/history/{rolePeer}")).StatusCode==HttpStatusCode.Forbidden && (await b.GetAsync("api/messenger/files/"+retainedFile)).StatusCode==HttpStatusCode.NotFound,"deleted group denies history and file access even to creator");
        using(var auditScope=app.Services.CreateScope()) { var auditDb=auditScope.ServiceProvider.GetRequiredService<HelperDb>(); Check(await auditDb.ChatGroupMessages.AnyAsync(m=>m.GroupId==roleId) && await auditDb.ChatFiles.AnyAsync(f=>f.Id==retainedFile) && await auditDb.ChatGroupEvents.CountAsync(e=>e.GroupId==roleId)==9,"deletion retains history files and audited role actions on server"); }
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelperDb>();
            var renamed = await Messenger.ResolveAdUserAsync(new("bob.renamed@ad.test", "Борис Новое имя", "bob.renamed", new string('b', 32)), db, default);
            Check(renamed.Id == bob.Id && await db.ChatMessages.CountAsync(m => m.RecipientId == renamed.Id) == 56, "AD rename preserves user identity and message history");
            try { await Messenger.ResolveAdUserAsync(new("bob.renamed@ad.test", "Other", "bob.renamed", new string('d', 32)), db, default); throw new Exception("Account takeover"); }
            catch (UnauthorizedAccessException) { Check(true, "recreated AD account cannot inherit former user's messages"); }
            renamed.IsActive = false; await db.SaveChangesAsync();
        }
        using var disabled = await a.PostAsJsonAsync("api/messenger/send", new ChatSend(bob.Id, "Disabled", Guid.NewGuid().ToString())); Check(disabled.StatusCode == HttpStatusCode.BadRequest, "disabled account cannot receive new messages");
        Check((await b.GetAsync("api/messenger/users")).StatusCode == HttpStatusCode.Forbidden, "disabled account cannot read chat");
        windows.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Negotiate", "verified-bob");
        Check((await windows.GetAsync("api/messenger/windows")).StatusCode == HttpStatusCode.Forbidden, "Windows SSO cannot reactivate a disabled panel account");
        async Task<HttpResponseMessage> Broadcast(string id,string audience,bool urgent,byte[]? file=null)
        {
            using var form=new MultipartFormDataContent(); form.Add(new StringContent(id),"clientId"); form.Add(new StringContent(audience),"audience"); form.Add(new StringContent(urgent ? "true" : "false"),"urgent"); form.Add(new StringContent("Объявление"),"body"); if(file!=null) form.Add(new ByteArrayContent(file),"files","notice.pdf"); return await a.PostAsync("api/messenger/broadcast",form);
        }
        Check((await a.GetFromJsonAsync<JsonElement>("api/messenger/capabilities")).GetProperty("canBroadcast").GetBoolean(),"ordinary AD user can broadcast");

        Check((await Broadcast(Guid.NewGuid().ToString(),"online",false)).StatusCode==HttpStatusCode.BadRequest,"online broadcast excludes offline users");
        await using var charlieHub=new HubConnectionBuilder().WithUrl(address+"/messengerHub",o=>o.AccessTokenProvider=()=>Task.FromResult<string?>(charlie.Token)).Build(); await charlieHub.StartAsync();
        string broadcastId=Guid.NewGuid().ToString("N"); using var announcement=await Broadcast(broadcastId,"online",true,[4,5,6]); announcement.EnsureSuccessStatusCode(); Check((await announcement.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recipients").GetInt32()==1,"online broadcast snapshots connected recipients and skips disabled accounts");
        var announcementMessage=(await c.GetFromJsonAsync<List<ChatMessage>>("api/messenger/history/"+alice.Id))!.Single(m=>m.BroadcastId==broadcastId);
        Check(announcementMessage.IsUrgent && (await c.GetByteArrayAsync("api/messenger/files/"+announcementMessage.Attachments.Single().Id)).SequenceEqual(new byte[]{4,5,6}),"urgent broadcast includes downloadable attachments");
        using var duplicateAnnouncement=await Broadcast(broadcastId,"online",true,[4,5,6]); duplicateAnnouncement.EnsureSuccessStatusCode();
        Check((await c.GetFromJsonAsync<List<ChatMessage>>("api/messenger/history/"+alice.Id))!.Count(m=>m.BroadcastId==broadcastId)==1,"broadcast retry creates one copy per recipient");
        Check((await a.PostAsJsonAsync("api/messenger/ack/"+announcementMessage.Id,new{})).StatusCode==HttpStatusCode.Forbidden,"sender cannot acknowledge for recipient");
        Check((await c.GetFromJsonAsync<List<ChatMessage>>("api/messenger/urgent"))!.Any(m=>m.Id==announcementMessage.Id),"urgent queue includes unacknowledged broadcasts");
        (await c.PostAsJsonAsync("api/messenger/ack/"+announcementMessage.Id,new{})).EnsureSuccessStatusCode();
        Check(!(await c.GetFromJsonAsync<List<ChatMessage>>("api/messenger/urgent"))!.Any(m=>m.Id==announcementMessage.Id),"acknowledgment removes message from urgent queue");
        Check((await a.GetFromJsonAsync<JsonElement>("api/messenger/broadcasts"))[0].GetProperty("acknowledged").GetInt32()==1,"broadcast report counts recipient acknowledgments");
        Check((await Broadcast(Guid.NewGuid().ToString(),"all",false)).StatusCode==HttpStatusCode.TooManyRequests,"broadcast rate limit applies to new requests but permits retries");
        using(var scope=app.Services.CreateScope()) { var d=scope.ServiceProvider.GetRequiredService<HelperDb>(); foreach(var item in await d.ChatBroadcasts.ToListAsync())item.CreatedAt=DateTime.UtcNow.AddMinutes(-2);await d.SaveChangesAsync(); }
        await charlieHub.StopAsync(); using var allAnnouncement=await Broadcast(Guid.NewGuid().ToString(),"all",false); allAnnouncement.EnsureSuccessStatusCode(); Check((await allAnnouncement.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recipients").GetInt32()==1,"all-user broadcast persists for offline active users");
        using(var scope=app.Services.CreateScope()) { var d=scope.ServiceProvider.GetRequiredService<HelperDb>(); foreach(var item in await d.ChatBroadcasts.ToListAsync())item.CreatedAt=DateTime.UtcNow.AddMinutes(-2); foreach(var item in await d.ChatMessages.Where(m=>m.IsUrgent).ToListAsync())item.SentAt=DateTime.UtcNow.AddHours(-2);await d.SaveChangesAsync(); }
        Check((await c.GetFromJsonAsync<List<ChatMessage>>("api/messenger/urgent"))!.Count==0,"expired urgent broadcasts remain in history but leave alert queue");
        string selectedId=Guid.NewGuid().ToString("N");
        using(var selectedForm=new MultipartFormDataContent()) { selectedForm.Add(new StringContent(selectedId),"clientId"); selectedForm.Add(new StringContent("selected"),"audience");selectedForm.Add(new StringContent("Выбранному сотруднику"),"body");selectedForm.Add(new StringContent(charlie.Id.ToString()),"recipients");using var response=await a.PostAsync("api/messenger/broadcast",selectedForm);response.EnsureSuccessStatusCode();Check((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recipients").GetInt32()==1,"selected broadcast is available to ordinary AD users"); }
        using(var invalidForm=new MultipartFormDataContent()) { invalidForm.Add(new StringContent(Guid.NewGuid().ToString()),"clientId");invalidForm.Add(new StringContent("selected"),"audience");invalidForm.Add(new StringContent("Ошибка"),"body");invalidForm.Add(new StringContent(bob.Id.ToString()),"recipients");Check((await a.PostAsync("api/messenger/broadcast",invalidForm)).StatusCode==HttpStatusCode.BadRequest,"selected broadcast rejects disabled recipients"); }
        string commandId=Guid.NewGuid().ToString("N");
        using(var response=await a.PostAsJsonAsync("api/messenger/send",new ChatSend(charlie.Id,"/срочно /танец Проверка команд",commandId))) { response.EnsureSuccessStatusCode();var row=await response.Content.ReadFromJsonAsync<ChatMessage>();Check(row!.Body=="Проверка команд"&&row.IsUrgent&&row.Command=="dance","command prefixes are stripped and metadata stored for private messages"); }
        using(var commandRetry=await a.PostAsJsonAsync("api/messenger/send",new ChatSend(charlie.Id,"/срочно /танец Проверка команд",commandId)))commandRetry.EnsureSuccessStatusCode();
        Check((await a.PostAsJsonAsync("api/messenger/send",new ChatSend(charlie.Id,"Проверка команд",commandId))).StatusCode==HttpStatusCode.BadRequest,"commandRetry cannot change command metadata");
        using(var response=await a.PostAsJsonAsync("api/messenger/send",new ChatSend(charlie.Id,"/танец",Guid.NewGuid().ToString()))) { response.EnsureSuccessStatusCode();var row=await response.Content.ReadFromJsonAsync<ChatMessage>();Check(row!.Body==""&&row.Command=="dance","animation-only command has no visible text"); }
        Check((await a.PostAsJsonAsync("api/messenger/send",new ChatSend(charlie.Id,"/срочно",Guid.NewGuid().ToString()))).StatusCode==HttpStatusCode.BadRequest,"urgent command requires text");
        Check((await a.PostAsJsonAsync("api/messenger/send",new ChatSend(charlie.Id,"/неизвестно текст",Guid.NewGuid().ToString()))).StatusCode==HttpStatusCode.BadRequest,"unknown commands are rejected rather than displayed or executed");
        using(var response=await a.PostAsJsonAsync("api/messenger/groups",new GroupCreate("Команды",new[]{charlie.Id}))) { response.EnsureSuccessStatusCode();int commandGroupPeer=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();using var commandSent=await a.PostAsJsonAsync("api/messenger/send",new ChatSend(commandGroupPeer,"/срочно /танец В группе",Guid.NewGuid().ToString()));commandSent.EnsureSuccessStatusCode();var row=await commandSent.Content.ReadFromJsonAsync<ChatMessage>();Check(row!.Command=="dance"&&row.IsUrgent&&row.Body=="В группе","group command metadata survives delivery");Check((await c.GetFromJsonAsync<List<ChatMessage>>("api/messenger/effects"))!.Any(m=>m.RecipientId==commandGroupPeer&&m.Command=="dance"),"group members receive pending effects after offline reconnect"); }
        var options = app.Services.GetRequiredService<MessengerSettings>(); options.Save(new(false));
        Check((await a.GetAsync("api/messenger/users")).StatusCode == HttpStatusCode.Forbidden, "disabled messenger blocks chat APIs");
        Check((await windows.GetAsync("api/messenger/windows")).StatusCode == HttpStatusCode.Forbidden, "disabled messenger blocks Windows SSO");
        Check(new MessengerSettings(builder.Configuration).Value.Enabled == false, "messenger settings survive restart");
        await app.StopAsync();
        Console.WriteLine("ALL MESSENGER CHECKS PASSED");
    }
}
