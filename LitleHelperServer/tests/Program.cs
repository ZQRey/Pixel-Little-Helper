using Microsoft.AspNetCore.SignalR.Client;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

static void Check(bool result, string text) { if (!result) throw new Exception(text); Console.WriteLine("PASS " + text); }
if (args.FirstOrDefault() == "--permissions-schema")
{
    using var db = new LitleHelperServer.HelperDb(new DbContextOptionsBuilder<LitleHelperServer.HelperDb>().UseSqlite("Data Source=:memory:").Options);
    await db.Database.OpenConnectionAsync(); await db.Database.EnsureCreatedAsync();
    db.Users.Add(new() { Username = "legacy", PasswordHash = "!AD", Role = "User" }); await db.SaveChangesAsync();
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE Users DROP COLUMN PermissionOverrides");
    await LitleHelperServer.Access.EnsureSchema(db); db.ChangeTracker.Clear();
    var user = await db.Users.SingleAsync();
    Check(user.Username == "legacy" && user.Permissions.Count == 0, "legacy SQLite users preserved with empty overrides");
    user.Permissions = new() { ["users.manage"] = true }; await db.SaveChangesAsync();
    await LitleHelperServer.Access.EnsureSchema(db); db.ChangeTracker.Clear();
    Check((await db.Users.SingleAsync()).EffectivePermissions.Contains("users.manage"), "repeated migration preserves individual permissions");
    return;
}
if (args.FirstOrDefault() == "--ad-referrals")
{
    await using var ldap = new MockAd();
    string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Integrations:SettingsFile"] = file }).Build();
    var settings = new LitleHelperServer.IntegrationSettings(configuration, new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
    try
    {
        settings.SaveAd(new(true, "localhost", ldap.Port, "ad.test", "AD", "DC=ad,DC=test", ldap.Pem));
        var auth = new LitleHelperServer.AdAuthentication(settings, Microsoft.Extensions.Logging.Abstractions.NullLogger<LitleHelperServer.AdAuthentication>.Instance);
        var identity = await auth.AuthenticateAsync(new("AD\\alice", "AD-test-password"), CancellationToken.None);
        Check(identity.Username == "alice@ad.test", "AD user with subordinate LDAP referral can sign in");
        try { await auth.AuthenticateAsync(new("alice", "wrong"), CancellationToken.None); throw new Exception("Wrong password accepted"); }
        catch (UnauthorizedAccessException) { Check(true, "wrong AD password still rejected"); }
    }
    finally { Directory.Delete(Path.GetDirectoryName(file)!, true); }
    return;
}
if (args.FirstOrDefault() == "--ad-probe")
{
    await using var ldap = new MockAd();
    var options = new Novell.Directory.Ldap.LdapConnectionOptions().UseSsl().ConfigureRemoteCertificateValidationCallback((_, cert, _, errors) =>
    {
        Console.WriteLine("TLS errors: " + errors);
        using var chain = new System.Security.Cryptography.X509Certificates.X509Chain();
        chain.ChainPolicy.TrustMode = System.Security.Cryptography.X509Certificates.X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.ImportFromPem(ldap.Pem); chain.ChainPolicy.RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;
        bool valid = chain.Build(new System.Security.Cryptography.X509Certificates.X509Certificate2(cert!));
        Console.WriteLine("Custom CA valid: " + valid + "; " + string.Join(",", chain.ChainStatus.Select(x => x.Status.ToString()))); return valid;
    });
    using var connection = new Novell.Directory.Ldap.LdapConnection(options);
    await connection.ConnectAsync("localhost", ldap.Port); await connection.BindAsync("alice@ad.test", "AD-test-password");
    Console.WriteLine("LDAP bind successful"); return;
}
if (args.FirstOrDefault() == "--telegram-management") { await TelegramManagementTests.RunAsync(); return; }
string url = args.FirstOrDefault() ?? "http://127.0.0.1:21500";
string mockUrl = args.Skip(1).FirstOrDefault() ?? "http://127.0.0.1:21501";
var mockBuilder = WebApplication.CreateBuilder();
mockBuilder.Logging.ClearProviders(); mockBuilder.WebHost.UseUrls(mockUrl);
var mock = mockBuilder.Build();
int initCount = 0, killCount = 0, requester = 0;
int ticketCount = 0;
string mockMode = "token";
string content = "";
int telegramMessages = 0;
bool telegramFail = false;
string telegramText = "";
mock.MapPost("/bot{botToken}/sendMessage", async (string botToken, HttpRequest request) =>
{
    Check(botToken == "123456:mock-token", "Telegram uses configured bot token");
    using var body = await JsonDocument.ParseAsync(request.Body);
    Check(body.RootElement.GetProperty("chat_id").GetString() == "-100888" && body.RootElement.GetProperty("message_thread_id").GetInt32() == 7, "Telegram group and topic configured");
    if (telegramFail) return Results.Json(new { ok = false }, statusCode: 429);
    telegramText = body.RootElement.GetProperty("text").GetString()!; telegramMessages++;
    return Results.Ok(new { ok = true, result = new { message_id = telegramMessages } });
});
mock.MapGet("/apirest.php/initSession", (HttpRequest request) =>
{
    if (mockMode == "disabled") return Results.Json(new[] { "ERROR", "API отключено" }, statusCode: 400);
    if (mockMode == "denied-ip") return Results.Json(new[] { "ERROR_NOT_ALLOWED_IP", "Not allowed" }, statusCode: 401);
    if (mockMode == "bad-token") return Results.Json(new[] { "ERROR_GLPI_LOGIN_USER_TOKEN", "Invalid token" }, statusCode: 401);
    if (mockMode == "html") return Results.Text("<html>Login</html>", "text/html");
    if (mockMode == "basic")
    {
        Check(request.Headers.Authorization == "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("mock-login:mock-password")), "GLPI Basic authentication headers");
        initCount++; return Results.Ok(new { session_token = "mock-session" });
    }
    Check(request.Headers["App-Token"] == "mock-app" && request.Headers.Authorization == "user_token mock-user", "GLPI init authentication headers");
    initCount++; return Results.Ok(new { session_token = "mock-session" });
});
mock.MapGet("/apirest.php/search/User", (HttpRequest request) =>
{
    Check(request.Headers["Session-Token"] == "mock-session" && request.Query["criteria[0][field]"] == "1" && request.Query["forcedisplay[0]"] == "2", "GLPI exact login search and requester ID field");
    return Results.Json(new { data = new[] { new Dictionary<string, object> { ["1"] = "alice", ["2"] = 42 } } });
});
mock.MapPost("/apirest.php/Ticket", async (HttpRequest request) =>
{
    ticketCount++;
    using var body = await JsonDocument.ParseAsync(request.Body); var input = body.RootElement.GetProperty("input");
    requester = input.GetProperty("_users_id_requester").GetInt32(); content = input.GetProperty("content").GetString()!;
    return Results.Ok(new { id = 7001 });
});
var ticketStatuses = new Dictionary<int, int>();
mock.MapGet("/apirest.php/Ticket/{id:int}", (int id) => Results.Ok(new { id, status = ticketStatuses.GetValueOrDefault(id, 1) }));
mock.MapGet("/apirest.php/Ticket/{id:int}/Ticket_User", () => Results.Json(Array.Empty<object>()));
mock.MapPut("/apirest.php/Ticket/{id:int}", async (int id, HttpRequest request) => { using var body = await JsonDocument.ParseAsync(request.Body); ticketStatuses[id] = body.RootElement.GetProperty("input").GetProperty("status").GetInt32(); return Results.Ok(new { id }); });
mock.MapGet("/apirest.php/killSession", () => { killCount++; return Results.Ok(); });
await mock.StartAsync();
using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(20) };
async Task<HttpResponseMessage> Call(string path, string? auth = null, object? body = null, HttpMethod? method = null)
{
    var request = new HttpRequestMessage(method ?? (body == null ? HttpMethod.Get : HttpMethod.Post), "/api" + path);
    if (auth != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth);
    if (body != null) request.Content = JsonContent.Create(body);
    return await http.SendAsync(request);
}
async Task<JsonElement> Json(string path, string? auth = null, object? body = null, HttpMethod? method = null)
{
    using var response = await Call(path, auth, body, method);
    string text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) throw new Exception(path + ": " + response.StatusCode + " " + text);
    return JsonSerializer.Deserialize<JsonElement>(text);
}
async Task<string> Login(string username, string password)
{
    var login = await Json("/auth/login", body: new { username, password }); return login.GetProperty("token").GetString()!;
}
async Task<string> Change(string auth, string oldPass, string newPass)
{
    var changed = await Json("/auth/change-password", auth, new { currentPassword = oldPass, newPassword = newPass }); return changed.GetProperty("token").GetString()!;
}
try
{
    using var anonymous = await Call("/computers"); Check(anonymous.StatusCode == HttpStatusCode.Unauthorized, "anonymous access denied");
    string admin = await Login("admin", "admin123");
    using var required = await Call("/computers", admin); Check(required.StatusCode == HttpStatusCode.Forbidden, "default password blocks administration");
    using var shortPassword = await Call("/auth/change-password", admin, new { currentPassword = "admin123", newPassword = "Short7!" });
    Check(shortPassword.StatusCode == HttpStatusCode.BadRequest, "seven-character password rejected");
    admin = await Change(admin, "admin123", "Admin8!x");
    admin = await Login("admin", "Admin8!x");
    Check(true, "eight-character password accepted and login succeeds");
    var staffTokens = new Dictionary<string, string>();
    foreach (var (name, role) in new[] { ("manager", "Admin"), ("operator", "Operator"), ("alice", "User") })
    {
        await Json("/users", admin, new { username = name, fullName = name, role, isActive = true, password = "Start8!x" });
        string initial = await Login(name, "Start8!x");
        staffTokens[name] = await Change(initial, "Start8!x", "Change8!");
    }
    using var operatorComputers = await Call("/computers", staffTokens["operator"]); Check(operatorComputers.IsSuccessStatusCode, "Operator may view status");
    using var userComputers = await Call("/computers", staffTokens["alice"]); Check(userComputers.StatusCode == HttpStatusCode.Forbidden, "User cannot view computers");
    using var operatorButtons = await Call("/buttons", staffTokens["operator"]); Check(operatorButtons.StatusCode == HttpStatusCode.Forbidden, "Operator cannot manage buttons");
    var enrolled = await Json("/computers/enroll", admin, new { machineName = "TEST-PC" });
    string key = enrolled.GetProperty("clientToken").GetString()!;
    await using var agent = new HubConnectionBuilder().WithUrl(url + "/helperHub", options =>
    { options.Headers["X-Client-Key"] = key; options.Headers["X-Machine-Name"] = "TEST-PC"; }).Build();
    var pushed = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
    agent.On<JsonElement>("OnButtonsUpdated", buttons => pushed.TrySetResult(buttons));
    var delivered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    agent.On<JsonElement>("ExecuteCommand", async envelope =>
    {
        string taskId = envelope.GetProperty("taskId").GetString()!;
        if (envelope.GetProperty("type").GetString() == "cmd") delivered.TrySetResult("cmd");
        await agent.InvokeAsync("SendExecutionOutput", taskId, "test-stream\n");
        await agent.InvokeAsync("SendExecutionResult", taskId, "test-stream\ntest-completed", 0);
    });
    await agent.StartAsync();
    var buttons = await agent.InvokeAsync<JsonElement>("RegisterComputer", new { machineName = "TEST-PC", userName = "alice", domainName = "TEST", ipAddress = "127.0.0.1", osVersion = "Test OS" });
    Check(buttons.GetArrayLength() == 4, "authenticated agent registration and server menu");
    await agent.InvokeAsync("UpdateHardwareAndSoftware", new { cpuModel = "Test CPU", totalRamBytes = 8589934592L }, new[] { new { name = "Test Software", version = "1.0" } });
    var list = await Json("/computers", admin); int computerId = list[0].GetProperty("id").GetInt32();
    Check(list[0].GetProperty("isOnline").GetBoolean(), "computer online immediately");
    using var operatorProcesses = await Call("/commands", staffTokens["operator"], new { machines = new[] { "TEST-PC" }, type = "processes", payload = "" });
    Check(operatorProcesses.StatusCode == HttpStatusCode.Forbidden, "Operator cannot request process list");
    using var massProcesses = await Call("/commands", admin, new { machines = new[] { "ALL" }, type = "processes", payload = "" });
    Check(massProcesses.StatusCode == HttpStatusCode.BadRequest, "process listing requires one specific computer");
    using var invalidPid = await Call("/commands", admin, new { machines = new[] { "TEST-PC" }, type = "kill_pid", payload = "{\"pid\":4,\"name\":\"System\",\"startTimeUtcTicks\":\"1\"}" });
    Check(invalidPid.StatusCode == HttpStatusCode.BadRequest, "invalid protected process target rejected");
    var processTasks = await Json("/commands", admin, new { machines = new[] { "TEST-PC" }, type = "processes", payload = "" });
    string processTaskId = processTasks[0].GetProperty("taskId").GetString()!;
    using var processTask = await Call("/tasks/" + processTaskId, admin);
    Check(processTask.IsSuccessStatusCode, "creator can poll process request result");
    using var otherAdminTask = await Call("/tasks/" + processTaskId, staffTokens["manager"]);
    Check(otherAdminTask.StatusCode == HttpStatusCode.NotFound, "another Admin cannot read process request result");
    var inventory = await Json($"/computers/{computerId}/inventory", admin);
    Check(inventory.GetProperty("hardware").GetProperty("cpuModel").GetString() == "Test CPU", "inventory persisted");
    foreach (string restricted in new[] { "manager", "operator", "alice" })
    { using var forbidden = await Call($"/computers/{computerId}/inventory", staffTokens[restricted]); Check(forbidden.StatusCode == HttpStatusCode.Forbidden, restricted + " cannot read inventory"); }
    using var rawDenied = await Call("/commands", staffTokens["manager"], new { machines = new[] { "TEST-PC" }, type = "cmd", payload = "echo test" });
    Check(rawDenied.StatusCode == HttpStatusCode.Forbidden, "Admin cannot bypass terminal RBAC via API");
    using var buttonDenied = await Call("/buttons", staffTokens["manager"], new { title = "escape", actionType = "run_command", payload = "cmd.exe", targetGroup = "All" });
    Check(buttonDenied.StatusCode == HttpStatusCode.Forbidden, "Admin cannot bypass terminal RBAC via command button");
    var tasks = await Json("/commands", admin, new { machines = new[] { "TEST-PC" }, type = "cmd", payload = "echo test" });
    Check(await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10)) == "cmd", "command delivered with correlation ID");
    JsonElement audit = default;
    for (int i = 0; i < 30; i++) { audit = await Json("/audit", admin); if (audit[0].GetProperty("status").GetString() == "Completed") break; await Task.Delay(100); }
    Check(audit[0].GetProperty("result").GetString()!.Contains("test-completed"), "stream and result persisted in audit");
    bool spoofRejected = false;
    try { await agent.InvokeAsync("SendExecutionResult", "unknown-task", "spoof", 0); } catch { spoofRejected = true; }
    Check(spoofRejected, "unissued task result rejected");
    var added = await Json("/buttons", staffTokens["manager"], new { title = "New URL", actionType = "open_url", payload = "https://example.org", targetGroup = "All", isActive = true, orderIndex = 10 });
    pushed = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
    await Json("/buttons/apply", admin, new { });
    Check((await pushed.Task.WaitAsync(TimeSpan.FromSeconds(10))).GetArrayLength() == 5, "button changes broadcast to agent");
    int ticketId = await agent.InvokeAsync<int>("CreateTicket", "Test issue", "Integration test description");
    Check(ticketId == 7001 && requester == 42 && content.Contains("TEST-PC") && content.Contains("alice"), "GLPI ticket requester and PC identity");
    Check(initCount == killCount && initCount == 1, "GLPI session always released");
    var ownTickets = await Json("/tickets", staffTokens["alice"]); Check(ownTickets.GetArrayLength() == 1, "User sees own agent-created ticket");
    await Json("/tickets", staffTokens["operator"], new { title = "Operator ticket", description = "Operator issue" });
    ownTickets = await Json("/tickets", staffTokens["alice"]); Check(ownTickets.GetArrayLength() == 1, "User cannot enumerate other users' tickets");
    var allTickets = await Json("/tickets", admin);
    int otherId = allTickets.EnumerateArray().First(x => x.GetProperty("username").GetString() == "operator").GetProperty("id").GetInt32();
    await Json("/tickets/" + otherId + "/status", admin, new { status = 4 }, HttpMethod.Put);
    Check((await Json("/tickets?status=4", admin)).GetArrayLength() == 1, "ticket status filter selects waiting tickets");
    Check((await Json("/tickets?status=4", staffTokens["alice"])).GetArrayLength() == 0, "ticket status filter preserves owner isolation");
    using (var badFilter = await Call("/tickets?status=unknown", admin)) Check(badFilter.StatusCode == HttpStatusCode.BadRequest, "invalid status filter rejected");
    Check((await Json("/tickets?status=active", admin)).GetArrayLength() == 2, "active status filter includes new and waiting tickets");
    using var other = await Call("/tickets/" + otherId, staffTokens["alice"]); Check(other.StatusCode == HttpStatusCode.Forbidden, "User cannot fetch another ticket by ID");
    await using var badAgent = new HubConnectionBuilder().WithUrl(url + "/helperHub", o => { o.Headers["X-Client-Key"] = "incorrect"; o.Headers["X-Machine-Name"] = "TEST-PC"; }).Build();
    bool badRejected = false; try { await badAgent.StartAsync(); } catch { badRejected = true; } Check(badRejected, "incorrect agent key rejected");
    await using var operatorHub = new HubConnectionBuilder().WithUrl(url + "/helperHub", o => o.AccessTokenProvider = () => Task.FromResult<string?>(staffTokens["operator"])).Build();
    await operatorHub.StartAsync();
    bool rpcDenied = false; try { await operatorHub.InvokeAsync<JsonElement>("SendCommandToClient", "TEST-PC", "cmd", "echo denied"); } catch { rpcDenied = true; }
    Check(rpcDenied, "Operator cannot bypass RBAC through hub RPC");
    await agent.StopAsync();
    for (int i = 0; i < 30; i++) { list = await Json("/computers", admin); if (!list[0].GetProperty("isOnline").GetBoolean()) break; await Task.Delay(100); }
    Check(!list[0].GetProperty("isOnline").GetBoolean(), "disconnect marks computer offline");
    using var last = await Call("/users/1", admin, method: HttpMethod.Delete); Check(last.StatusCode == HttpStatusCode.BadRequest, "last SuperAdmin protected");
    foreach (string role in new[] { "manager", "operator", "alice" })
    {
        using var deniedSettings = await Call("/settings/glpi", staffTokens[role]);
        Check(deniedSettings.StatusCode == HttpStatusCode.Forbidden, role + " cannot access GLPI secrets/settings");
    }
    var settingsBefore = await Json("/settings/glpi", admin);
    Check(settingsBefore.GetProperty("hasUserToken").GetBoolean() && !settingsBefore.GetRawText().Contains("mock-user"), "GLPI settings expose flags without secrets");
    using var invalidUrl = await Call("/settings/glpi", admin, new { baseUrl = "file:///tmp/test", serviceUserId = 0, authMode = "token", login = "" }, HttpMethod.Put);
    Check(invalidUrl.StatusCode == HttpStatusCode.BadRequest, "GLPI settings reject non-HTTP URL");
    var savedSettings = await Json("/settings/glpi", admin, new { baseUrl = mockUrl, appToken = "", userToken = "", serviceUserId = 99, authMode = "token", login = "" }, HttpMethod.Put);
    Check(savedSettings.GetProperty("baseUrl").GetString() == mockUrl + "/apirest.php" && savedSettings.GetProperty("hasUserToken").GetBoolean(), "GLPI URL normalized and blank secrets preserved");
    int ticketsBeforeTest = ticketCount;
    var connected = await Json("/settings/glpi/test", admin, new { });
    Check(connected.GetProperty("success").GetBoolean() && ticketCount == ticketsBeforeTest && initCount == killCount, "GLPI connection test releases session without creating ticket");
    foreach (var (mode, phrase) in new[] { ("disabled", "API отключён"), ("denied-ip", "не разрешает IP"), ("bad-token", "отклонил учётные данные"), ("html", "страницу входа") })
    {
        mockMode = mode;
        var diagnosis = await Json("/settings/glpi/test", admin, new { });
        Check(!diagnosis.GetProperty("success").GetBoolean() && diagnosis.GetProperty("message").GetString()!.Contains(phrase), "GLPI diagnostic explains " + mode);
    }
    mockMode = "basic";
    var basicSettings = await Json("/settings/glpi", admin, new { baseUrl = mockUrl, serviceUserId = 99, authMode = "password", login = "mock-login", password = "mock-password" }, HttpMethod.Put);
    Check(basicSettings.GetProperty("hasPassword").GetBoolean() && !basicSettings.GetRawText().Contains("mock-password"), "GLPI password saved without returning it");
    connected = await Json("/settings/glpi/test", admin, new { });
    Check(connected.GetProperty("success").GetBoolean(), "GLPI login/password mode works");
    if (args.Length > 2)
    {
        string encrypted = await File.ReadAllTextAsync(args[2]);
        Check(!encrypted.Contains("mock-password") && !encrypted.Contains("mock-user") && !encrypted.Contains("mock-app"), "persisted GLPI secrets encrypted at rest");
    }
    var cleared = await Json("/settings/glpi", admin, new { baseUrl = mockUrl, serviceUserId = 99, authMode = "token", login = "", clearAppToken = true, clearUserToken = true, clearPassword = true }, HttpMethod.Put);
    Check(!cleared.GetProperty("hasAppToken").GetBoolean() && !cleared.GetProperty("hasUserToken").GetBoolean() && !cleared.GetProperty("hasPassword").GetBoolean(), "explicit clear removes saved GLPI secrets");
    foreach (string restricted in new[] { "manager", "operator", "alice" })
    foreach (string integration in new[] { "telegram", "ad" })
    {
        using var denied = await Call("/settings/" + integration, staffTokens[restricted]);
        Check(denied.StatusCode == HttpStatusCode.Forbidden, restricted + " cannot access " + integration + " settings");
    }
    using var disabledAd = await Call("/auth/ad", body: new { username = "alice", password = "wrong" });
    Check(disabledAd.StatusCode == HttpStatusCode.Unauthorized, "AD login disabled until configured");
    var telegramOptions = await Json("/settings/telegram", admin, new { enabled = false, botToken = "123456:mock-token", chatId = "-100888", threadId = 7 }, HttpMethod.Put);
    Check(telegramOptions.GetProperty("hasBotToken").GetBoolean() && !telegramOptions.GetRawText().Contains("mock-token"), "Telegram token never returned to browser");
    telegramOptions = await Json("/settings/telegram", admin, new { enabled = true, botToken = "", chatId = "-100888", threadId = 7 }, HttpMethod.Put);
    Check(telegramOptions.GetProperty("hasBotToken").GetBoolean(), "blank Telegram token preserves saved secret");
    var telegramTest = await Json("/settings/telegram/test", admin, new { });
    Check(telegramTest.GetProperty("success").GetBoolean() && telegramMessages == 1, "explicit Telegram test sends message");
    await Json("/settings/glpi", admin, new { baseUrl = mockUrl, appToken = "mock-app", userToken = "mock-user", serviceUserId = 99, authMode = "token", login = "" }, HttpMethod.Put);
    mockMode = "token"; telegramFail = true;
    await Json("/tickets", admin, new { title = "Notification test", description = "Telegram failure must not cancel ticket" });
    await agent.StartAsync(); await agent.InvokeAsync<int>("CreateTicket", "Agent notification", "Created from pet"); await agent.StopAsync();
    var notificationState = await Json("/settings/telegram", admin);
    Check(notificationState.GetProperty("pending").GetInt32() == 2, "panel and agent tickets persist notifications in outbox");
    for (int i = 0; i < 50; i++) { notificationState = await Json("/settings/telegram", admin); if (notificationState.GetProperty("lastError").ValueKind == JsonValueKind.String) break; await Task.Delay(100); }
    Check(notificationState.GetProperty("lastError").GetString()!.Contains("лимит"), "Telegram failure recorded without failing ticket creation");
    telegramFail = false;
    for (int i = 0; i < 70 && telegramMessages < 3; i++) await Task.Delay(500);
    Check(telegramMessages == 3 && telegramText.Contains("GLPI #7001"), "outbox retries Telegram and delivers both tickets");
    await using var adMock = new MockAd();
    var adOptions = new { enabled = true, host = "localhost", port = adMock.Port, domain = "ad.test", netbiosDomain = "AD", baseDn = "DC=ad,DC=test", caCertificate = adMock.Pem };
    await Json("/settings/ad", admin, adOptions, HttpMethod.Put);
    var adTest = await Json("/settings/ad/test", admin, new { });
    Check(adTest.GetProperty("success").GetBoolean(), "LDAPS connection validates supplied CA certificate");
    using var wrongAd = await Call("/auth/ad", body: new { username = "alice", password = "wrong" });
    Check(wrongAd.StatusCode == HttpStatusCode.Unauthorized, "incorrect AD password rejected");
    var adLogin = await Json("/auth/ad", body: new { username = "AD\\alice", password = "AD-test-password" });
    string adToken = adLogin.GetProperty("token").GetString()!; int adId = adLogin.GetProperty("user").GetProperty("id").GetInt32();
    Check(adLogin.GetProperty("user").GetProperty("role").GetString() == "User" && adLogin.GetProperty("user").GetProperty("authSource").GetString() == "AD", "first AD login creates User role without stored password");
    var adTickets = await Json("/tickets", adToken);
    Check(adTickets.GetArrayLength() == 2 && adTickets.EnumerateArray().All(t => t.GetProperty("username").GetString() == "alice"), "AD UPN maps to own agent tickets only");
    using var adOther = await Call("/tickets/" + otherId, adToken); Check(adOther.StatusCode == HttpStatusCode.Forbidden, "AD user cannot fetch another user's ticket");
    using var adCreate = await Call("/tickets", adToken, new { title = "Denied", description = "Denied" }); Check(adCreate.StatusCode == HttpStatusCode.Forbidden, "AD default User has read-only ticket access");
    using var adPassword = await Call("/auth/change-password", adToken, new { currentPassword = "AD-test-password", newPassword = "New-password" }); Check(adPassword.StatusCode == HttpStatusCode.BadRequest, "AD password cannot be changed locally");
    using var localAdLogin = await Call("/auth/login", body: new { username = "alice@ad.test", password = "AD-test-password" }); Check(localAdLogin.StatusCode == HttpStatusCode.Unauthorized, "AD account cannot authenticate through local password endpoint");
    await Json("/users/" + adId, admin, new { username = "alice@ad.test", fullName = "Alice AD", role = "User", isActive = false }, HttpMethod.Put);
    using var revokedAd = await Call("/tickets", adToken); Check(revokedAd.StatusCode == HttpStatusCode.Unauthorized, "disabled AD user token revoked immediately");
    using var blockedAd = await Call("/auth/ad", body: new { username = "alice@ad.test", password = "AD-test-password" }); Check(blockedAd.StatusCode == HttpStatusCode.Unauthorized, "disabled AD user cannot reactivate by signing in");
    if (args.Length > 3) { string stored = File.ReadAllText(args[3]); Check(!stored.Contains("123456:mock-token") && !stored.Contains("AD-test-password"), "Telegram token encrypted and AD user password never saved"); }
    using var unavailableUpdate = await http.GetAsync("/api/client-updates/latest"); Check(unavailableUpdate.StatusCode == HttpStatusCode.NoContent, "no update offered before publishing");
    foreach (string restricted in new[] { "manager", "operator", "alice" })
    { using var denied = await Call("/settings/updates", staffTokens[restricted]); Check(denied.StatusCode == HttpStatusCode.Forbidden, restricted + " cannot publish updates"); }
    if (args.Length > 4 && File.Exists(Path.Combine(args[4], "PixelHelper.update.json")))
    {
        string manifestText = File.ReadAllText(Path.Combine(args[4], "PixelHelper.update.json"));
        var signed = JsonSerializer.Deserialize<PixelHelper.Updates.ClientUpdateManifest>(manifestText, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Check(signed.Valid() && !(signed with { Version = "9.9.9" }).Valid(), "update signature verified and manifest tampering rejected");
        async Task<HttpResponseMessage> Upload(string manifest, bool corrupt = false)
        {
            using var form = new MultipartFormDataContent();
            byte[] bytes = File.ReadAllBytes(Path.Combine(args[4], "PixelHelper.msi")); if (corrupt) bytes[0] ^= 1;
            form.Add(new ByteArrayContent(bytes), "package", "package.msi"); form.Add(new StringContent(manifest), "manifest", "manifest.json");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/settings/updates/upload") { Content = form }; request.Headers.Authorization = new("Bearer", admin); return await http.SendAsync(request);
        }
        using var badSignature = await Upload(JsonSerializer.Serialize(signed with { Signature = "invalid" })); Check(badSignature.StatusCode == HttpStatusCode.BadRequest, "unsigned update rejected before publication");
        using var badHash = await Upload(manifestText, true); Check(badHash.StatusCode == HttpStatusCode.BadRequest, "modified MSI rejected by checksum");
        using var published = await Upload(manifestText); Check(published.IsSuccessStatusCode, "signed MSI published successfully");
        var latest = await http.GetFromJsonAsync<PixelHelper.Updates.ClientUpdateManifest>("/api/client-updates/latest"); Check(latest!.Version == signed.Version && latest.Valid(), "client receives signed release manifest");
        using var package = await http.GetAsync("/api/client-updates/package/" + signed.Sha256, HttpCompletionOption.ResponseHeadersRead); Check(package.IsSuccessStatusCode && package.Content.Headers.ContentLength == signed.Size, "client package download has expected size");
        using var duplicate = await Upload(manifestText); Check(duplicate.StatusCode == HttpStatusCode.BadRequest, "release downgrade or duplicate version blocked");
        await Json("/settings/updates", admin, new { enabled = false }, HttpMethod.Put);
        using var paused = await http.GetAsync("/api/client-updates/latest"); Check(paused.StatusCode == HttpStatusCode.NoContent, "administrator can pause client updates");
    }
    Console.WriteLine("ALL INTEGRATION CHECKS PASSED");
}
finally { await mock.StopAsync(); await mock.DisposeAsync(); }
