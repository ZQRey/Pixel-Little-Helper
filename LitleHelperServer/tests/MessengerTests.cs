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
            try { await next(); }
            catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException) { context.Response.StatusCode = ex is UnauthorizedAccessException ? 403 : 400; }
        });
        app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization(); app.MapMessenger();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelperDb>(); await db.Database.EnsureCreatedAsync(); await Messenger.EnsureSchemaAsync(db); await Messenger.EnsureSchemaAsync(db);
            Check(await db.ChatMessages.CountAsync() == 0, "chat schema initialization is idempotent");
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
        using var a = alice.Http; using var b = bob.Http; using var c = charlie.Http;
        Check((await a.GetAsync("api/settings/messenger")).StatusCode == HttpStatusCode.Forbidden, "messenger session cannot manage server settings");
        var users = await a.GetFromJsonAsync<JsonElement>("api/messenger/users");
        Check(users.GetArrayLength() == 2 && users.EnumerateArray().Any(u => u.GetProperty("fullName").GetString() == "Борис"), "directory uses AD display names and excludes self and local accounts");
        var received = new TaskCompletionSource<ChatMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var hub = new HubConnectionBuilder().WithUrl(address + "/messengerHub", o => o.AccessTokenProvider = () => Task.FromResult<string?>(bob.Token)).Build();
        hub.On<ChatMessage>("ChatMessage", m => received.TrySetResult(m)); await hub.StartAsync();
        var request = new ChatSend(bob.Id, "Привет, Борис!", Guid.NewGuid().ToString());
        using var sent = await a.PostAsJsonAsync("api/messenger/send", request); sent.EnsureSuccessStatusCode(); var message = await sent.Content.ReadFromJsonAsync<ChatMessage>();
        var delivered = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Check(delivered.Body == request.Body && delivered.SenderId == alice.Id, "real SignalR delivery carries authenticated sender and stored message");
        Check((await b.GetFromJsonAsync<JsonElement>("api/messenger/history/" + alice.Id)).GetArrayLength() == 1, "recipient sees persistent dialogue history");
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
        var options = app.Services.GetRequiredService<MessengerSettings>(); options.Save(new(false));
        Check((await a.GetAsync("api/messenger/users")).StatusCode == HttpStatusCode.Forbidden, "disabled messenger blocks chat APIs");
        Check(new MessengerSettings(builder.Configuration).Value.Enabled == false, "messenger settings survive restart");
        await app.StopAsync();
        Console.WriteLine("ALL MESSENGER CHECKS PASSED");
    }
}
