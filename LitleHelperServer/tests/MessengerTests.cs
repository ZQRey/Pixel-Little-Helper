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
        builder.Services.AddDbContext<HelperDb>(o => o.UseSqlite("Data Source=" + Path.Combine(folder, "chat.db")));
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
        Check((await b.GetFromJsonAsync<JsonElement>($"api/messenger/history/{groupPeer}")).GetArrayLength() == 0, "reinvited member does not regain old history");
        (await c.PostAsJsonAsync($"api/messenger/groups/{groupId}/rename", new GroupUpdate("Новая группа"))).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync($"api/messenger/groups/{groupId}/close", new { })).EnsureSuccessStatusCode();
        Check((await a.PostAsJsonAsync("api/messenger/send", new ChatSend(groupPeer, "Closed", Guid.NewGuid().ToString()))).StatusCode == HttpStatusCode.BadRequest, "closed group rejects messages");
        Check((await a.GetFromJsonAsync<JsonElement>($"api/messenger/history/{groupPeer}")).GetArrayLength() == 50, "closed group retains readable history");
        (await a.PostAsJsonAsync($"api/messenger/groups/{groupId}/leave", new { })).EnsureSuccessStatusCode();
        Check((await a.GetAsync($"api/messenger/history/{groupPeer}")).StatusCode == HttpStatusCode.Forbidden, "leaving closed group revokes access");
        using var invalid = await a.PostAsJsonAsync("api/messenger/send", new ChatSend(bob.Id, new string('x', 4001), Guid.NewGuid().ToString())); Check(invalid.StatusCode == HttpStatusCode.BadRequest, "oversized messages rejected");
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
        var options = app.Services.GetRequiredService<MessengerSettings>(); options.Save(new(false));
        Check((await a.GetAsync("api/messenger/users")).StatusCode == HttpStatusCode.Forbidden, "disabled messenger blocks chat APIs");
        Check((await windows.GetAsync("api/messenger/windows")).StatusCode == HttpStatusCode.Forbidden, "disabled messenger blocks Windows SSO");
        Check(new MessengerSettings(builder.Configuration).Value.Enabled == false, "messenger settings survive restart");
        await app.StopAsync();
        Console.WriteLine("ALL MESSENGER CHECKS PASSED");
    }
}
