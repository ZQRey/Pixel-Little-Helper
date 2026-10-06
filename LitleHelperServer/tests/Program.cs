using Microsoft.AspNetCore.SignalR.Client;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

static void Check(bool result, string text) { if (!result) throw new Exception(text); Console.WriteLine("PASS " + text); }
string url = args.FirstOrDefault() ?? "http://127.0.0.1:21500";
string mockUrl = args.Skip(1).FirstOrDefault() ?? "http://127.0.0.1:21501";
var mockBuilder = WebApplication.CreateBuilder();
mockBuilder.Logging.ClearProviders(); mockBuilder.WebHost.UseUrls(mockUrl);
var mock = mockBuilder.Build();
int initCount = 0, killCount = 0, requester = 0;
string content = "";
mock.MapGet("/apirest.php/initSession", (HttpRequest request) =>
{
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
    using var body = await JsonDocument.ParseAsync(request.Body); var input = body.RootElement.GetProperty("input");
    requester = input.GetProperty("_users_id_requester").GetInt32(); content = input.GetProperty("content").GetString()!;
    return Results.Ok(new { id = 7001 });
});
mock.MapGet("/apirest.php/Ticket/{id:int}", (int id) => Results.Ok(new { id, status = 1 }));
mock.MapPut("/apirest.php/Ticket/{id:int}", (int id) => Results.Ok(new { id }));
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
    Console.WriteLine("ALL INTEGRATION CHECKS PASSED");
}
finally { await mock.StopAsync(); await mock.DisposeAsync(); }
