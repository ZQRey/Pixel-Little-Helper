using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PixelHelper.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Diagnostics;

namespace LitleHelperServer;

public sealed record AiDiagnosticRequest(string ClientRequestId, string MachineName, string Tool, string TargetId = "");
public interface IAiDiagnosticDispatch
{
    Task<NetworkProbeResult> ClientAsync(PanelUser user, Computer computer, string checkId, NetworkProbeRequest request, CancellationToken token);
    Task<NetworkProbeResult> ServerAsync(NetworkProbeRequest request, CancellationToken token);
}
public sealed class AiDiagnosticDispatch(HelperDb db, IHubContext<HelperHub> hub) : IAiDiagnosticDispatch
{
    public Task<NetworkProbeResult> ServerAsync(NetworkProbeRequest request, CancellationToken token) => AiNetworkProbe.RunAsync(request, "server", token);
    public async Task<NetworkProbeResult> ClientAsync(PanelUser user, Computer computer, string checkId, NetworkProbeRequest request, CancellationToken token)
    {
        var uri = AiNetworkProbe.Validate(request);
        if (!computer.IsOnline || computer.ConnectionId == null) return new("client:" + computer.MachineName, uri.Host, DateTimeOffset.UtcNow, "Offline");
        if (!computer.SupportsAiDiagnostics) return new("client:" + computer.MachineName, uri.Host, DateTimeOffset.UtcNow, "ClientUpgradeRequired");
        var log = new AuditLog { TaskId = checkId, AdminUsername = user.Username, MachineName = computer.MachineName,
            CommandType = "ai_diagnostic", CommandPayload = JsonSerializer.Serialize(request), Status = "Pending" };
        db.AuditLogs.Add(log); await db.SaveChangesAsync(token);
        try
        {
            await hub.Clients.Client(computer.ConnectionId).SendAsync("ExecuteCommand", new CommandEnvelope(checkId, "ai_diagnostic", log.CommandPayload), token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            while (true)
            {
                await Task.Delay(250, timeout.Token); await db.Entry(log).ReloadAsync(timeout.Token);
                if (log.Status == "Pending") continue;
                if (log.Status != "Completed" || log.Result.Length > 8192) return new("client:" + computer.MachineName, uri.Host, DateTimeOffset.UtcNow, "ClientCheckFailed");
                var result = JsonSerializer.Deserialize<NetworkProbeResult>(log.Result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (result == null || result.Status is not ("ResponseReceived" or "RedirectNotFollowed" or "TimedOut" or "AddressNotAllowed" or "ConnectionOrTlsFailed") ||
                    result.HttpStatus is < 100 or > 599 || result.DnsMs is < 0 or > 120000 || result.ConnectMs is < 0 or > 120000 || result.HeadersMs is < 0 or > 120000)
                    return new("client:" + computer.MachineName, uri.Host, DateTimeOffset.UtcNow, "InvalidClientResult");
                return result with { Source = "client:" + computer.MachineName, Target = uri.Host, CheckedAt = DateTimeOffset.UtcNow };
            }
        }
        catch (OperationCanceledException)
        {
            await db.AuditLogs.Where(a => a.Id == log.Id && a.Status == "Pending").ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, token.IsCancellationRequested ? "Cancelled" : "TimedOut"));
            if (token.IsCancellationRequested) throw;
            return new("client:" + computer.MachineName, uri.Host, DateTimeOffset.UtcNow, "TimedOut");
        }
        catch (JsonException) { return new("client:" + computer.MachineName, uri.Host, DateTimeOffset.UtcNow, "InvalidClientResult"); }
    }
}

public sealed class AiDiagnostics(HelperDb db, AiSettingsStore store, AiGate gate, IAiDiagnosticDispatch dispatch, IServiceScopeFactory scopes)
{
    public static bool Can(PanelUser user) => user.Role is Roles.Admin or Roles.SuperAdmin && Access.Can(user, "ai.diagnostics") && Access.Can(user, "computers.view");
    public static void Require(PanelUser user) { if (!Can(user)) throw new UnauthorizedAccessException("Administrator diagnostic permission is required"); }
    public async Task<AiDiagnosticCheck> RunAsync(string sessionId, AiDiagnosticRequest request, ClaimsPrincipal principal, CancellationToken token)
    {
        var user = await AiApi.EitherUserAsync(db, principal, token); Require(user);
        var options = store.ForUser(user); AiApi.Ready(options);
        if (!options.DiagnosticsEnabled) throw new ArgumentException("Diagnostics are disabled");
        var session = await AiApi.OwnAsync(db, user, sessionId, token); await AiApi.SessionUserAsync(db, principal, session, token); AiApi.Ready(options, session);
        if (session.SecurityVersion != user.SecurityVersion) throw new UnauthorizedAccessException("Open a new conversation after account changes");
        if (!Guid.TryParse(request.ClientRequestId, out var clientId) || request.MachineName is null || request.MachineName.Length > 100 ||
            request.Tool is not ("computer.status" or "network.http.server" or "network.http.client")) throw new ArgumentException("Unknown diagnostic action");
        string machine = Security.Canonical(request.MachineName);
        var computer = await db.Computers.AsNoTracking().SingleOrDefaultAsync(c => c.MachineName == machine, token) ?? throw new ArgumentException("Computer not found");
        // Existing Admin computer access is global. Do not invent broader access for other roles.
        AiDiagnosticTarget? target = request.Tool == "computer.status" ? null : options.DiagnosticTargets.SingleOrDefault(t => t.Id == request.TargetId) ?? throw new ArgumentException("Target is outside the diagnostic allowlist");
        using var run = CancellationTokenSource.CreateLinkedTokenSource(token); run.CancelAfter(TimeSpan.FromSeconds(25));
        string gateKey = "diag-user:" + user.Id;
        AiDiagnosticCheck check;
        AiRequestLog audit;
        var clock = Stopwatch.StartNew();
        await gate.Mutations.WaitAsync(token);
        try
        {
            var prior = await db.AiDiagnosticChecks.AsNoTracking().SingleOrDefaultAsync(c => c.SessionId == sessionId && c.ClientRequestId == clientId.ToString("N"), token);
            if (prior != null) return prior;
            long limit = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
            if (await db.AiRequestLogs.CountAsync(c => c.OwnerUserId == user.Id && c.Backend == "Diagnostics" && c.CreatedAt >= limit, token) >= 10 || !gate.Start(gateKey, run))
                throw new ArgumentException("Diagnostic limit reached or check already running");
            check = new() { SessionId = sessionId, OwnerUserId = user.Id, ClientRequestId = clientId.ToString("N"), MachineName = machine, Tool = request.Tool, TargetId = target?.Id ?? "" };
            session.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            db.AiDiagnosticChecks.Add(check);
            audit = new() { Id = check.Id, OwnerUserId = user.Id, SessionId = sessionId, Backend = "Diagnostics", ModelId = request.Tool, CreatedAt = check.CreatedAt };
            db.AiRequestLogs.Add(audit);
            try { await db.SaveChangesAsync(token); } catch { gate.Finish(gateKey); throw; }
        }
        finally { gate.Mutations.Release(); }
        using var monitorStop = new CancellationTokenSource();
        Task monitor = MonitorAsync(user, session, run, monitorStop.Token);
        try
        {
            object result = request.Tool == "computer.status"
                ? new { source = "server-cache", machine = computer.MachineName, computer.IsOnline, computer.LastSeen, computer.SupportsAiDiagnostics, checkedAt = DateTimeOffset.UtcNow }
                : request.Tool == "network.http.server"
                    ? await dispatch.ServerAsync(new(target!.Url, target.AllowedAddresses), run.Token)
                    : await dispatch.ClientAsync(user, computer, check.Id, new(target!.Url, target.AllowedAddresses), run.Token);
            run.Token.ThrowIfCancellationRequested();
            check.ResultJson = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)); check.Status = "Completed";
        }
        catch (OperationCanceledException) { check.Status = "Cancelled"; check.ResultJson = "{}"; }
        catch (Exception error) when (error is not OutOfMemoryException) { check.Status = "Failed"; check.ResultJson = JsonSerializer.Serialize(new { error = "Diagnostic check failed" }); }
        finally { monitorStop.Cancel(); await monitor; }
        try { audit.Status = check.Status; audit.DurationMs = clock.ElapsedMilliseconds; await db.SaveChangesAsync(CancellationToken.None); return check; }
        finally { gate.Finish(gateKey); }
    }
    private async Task MonitorAsync(PanelUser original, AiSession session, CancellationTokenSource run, CancellationToken stop)
    {
        try
        {
            while (true)
            {
                await Task.Delay(500, stop); using var scope = scopes.CreateScope(); var current = scope.ServiceProvider.GetRequiredService<HelperDb>();
                var user = await current.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == original.Id, stop);
                var settings = user is null ? null : store.ForUser(user);
                if (user == null || !user.IsActive || user.MustChangePassword || user.SecurityVersion != original.SecurityVersion || !AiAccess.CanChannel(user, session.Channel) || !Can(user) ||
                    settings is not { Enabled: true, DiagnosticsEnabled: true } || settings.Version != session.SettingsVersion ||
                    !await current.AiSessions.AnyAsync(s => s.Id == session.Id && s.Status == "Active", stop)) { run.Cancel(); return; }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch { run.Cancel(); }
    }
    public async Task<AiDiagnosticCheck> ForPromptAsync(PanelUser user, string sessionId, string id, CancellationToken token)
    {
        Require(user); if (!store.Read().DiagnosticsEnabled) throw new UnauthorizedAccessException("Diagnostics disabled");
        long since = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();
        return await db.AiDiagnosticChecks.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.SessionId == sessionId && c.OwnerUserId == user.Id && c.Status == "Completed" && c.CreatedAt >= since, token)
            ?? throw new ArgumentException("Diagnostic result expired or unavailable; run a new check");
    }
}
