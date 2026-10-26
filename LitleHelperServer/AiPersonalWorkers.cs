using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;

namespace LitleHelperServer;

public sealed record AiPersonalWorker(int OwnerUserId, string Url, string Password, string Provider,
    bool Enabled = true, bool CredentialReady = false, string Version = "");
public sealed record AiWorkerRegistration(int OwnerUserId, string Url, string Password, string Provider);
public sealed record AiPersonalKey(string ApiKey);

public sealed partial class AiSettingsStore
{
    // The whole registry is encrypted, including endpoint and owner associations.
    private List<AiPersonalWorker> Workers() => File.Exists(path + ".workers")
        ? JsonSerializer.Deserialize<List<AiPersonalWorker>>(protector.Unprotect(File.ReadAllText(path + ".workers")))! : [];
    private void WriteWorkers(List<AiPersonalWorker> workers)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path + ".workers.tmp", protector.Protect(JsonSerializer.Serialize(workers)));
        File.Move(path + ".workers.tmp", path + ".workers", true);
    }
    private static string Endpoint(string url) => ValidateUrl(url, true).GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    public object WorkerViews()
    {
        lock (gate) return Workers().Select(w => new { w.OwnerUserId, w.Url, w.Provider, w.Enabled, w.CredentialReady }).ToArray();
    }
    public AiPersonalWorker? Worker(int owner)
    {
        lock (gate) return Workers().SingleOrDefault(w => w.OwnerUserId == owner);
    }
    public AiOptions WorkerRoute(int owner)
    {
        var worker = Worker(owner) ?? throw new ArgumentException("Personal worker is not assigned");
        return new AiOptions { WorkerOwnerId = owner, OpenCodeUrl = worker.Url, OpenCodePassword = worker.Password };
    }
    public void RegisterWorker(AiWorkerRegistration request)
    {
        lock (gate)
        {
            var options = Read(); var workers = Workers();
            string url = ValidateUrl(request.Url, true).AbsoluteUri;
            if (new Uri(url).AbsolutePath != "/" || request.OwnerUserId < 1 || request.Password is null || request.Password.Length is < 16 or > 4096 ||
                string.IsNullOrWhiteSpace(request.Provider) || request.Provider.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')) ||
                !options.Models.Any(m => m.Provider == request.Provider && m.Price == "Free"))
                throw new ArgumentException("Use a dedicated root worker URL, a strong password and an allowed free-model provider");
            if (workers.Count >= 32 || workers.Any(w => w.OwnerUserId == request.OwnerUserId || Endpoint(w.Url) == Endpoint(url)) || Endpoint(url) == Endpoint(options.OpenCodeUrl))
                throw new ArgumentException("Worker/user already reserved or worker limit reached; endpoints cannot be reassigned");
            workers.Add(new(request.OwnerUserId, url, request.Password, request.Provider, Version: Guid.NewGuid().ToString("N")));
            WriteWorkers(workers);
        }
    }
    public void ChangeWorker(int owner, bool? enabled = null, bool? ready = null)
    {
        lock (gate)
        {
            var workers = Workers(); int index = workers.FindIndex(w => w.OwnerUserId == owner);
            if (index < 0) throw new ArgumentException("Personal worker is not assigned");
            var old = workers[index];
            workers[index] = old with { Enabled = enabled ?? old.Enabled, CredentialReady = ready ?? old.CredentialReady, Version = Guid.NewGuid().ToString("N") };
            WriteWorkers(workers);
        }
    }
    public AiOptions ForUser(PanelUser user, bool secrets = false) => ForUser(user.Id, secrets, user.Role);
    public AiOptions ForUser(int owner, bool secrets = false) => ForUser(owner, secrets, null);
    private AiOptions ForUser(int owner, bool secrets, string? role)
    {
        lock (gate)
        {
            var options = Read(secrets);
            bool admin = role is Roles.Admin or Roles.SuperAdmin;
            options = options with { Enabled = options.Enabled && (!options.PilotOnly || admin || options.PilotUserIds.Contains(owner)) };
            if (options.Backend != "OpenCode" || options.OpenCodeMode != "Personal") return options;
            var worker = Workers().SingleOrDefault(w => w.OwnerUserId == owner);
            var models = worker is null ? [] : options.Models.Where(m => m.Provider == worker.Provider).ToArray();
            string model = models.Any(m => m.Id == options.DefaultModel) ? options.DefaultModel : models.FirstOrDefault(m => m.Price == "Free")?.Id ?? "";
            return options with { Enabled = options.Enabled && worker is { Enabled: true, CredentialReady: true } && model.Length > 0,
                Version = options.Version + ":" + (worker?.Version ?? "unassigned"), Models = models, DefaultModel = model,
                WorkerOwnerId = owner, OpenCodeUrl = worker?.Url ?? "http://unassigned:4096/", OpenCodePassword = secrets ? worker?.Password ?? "" : "", ApiKey = "" };
        }
    }
}

public static class AiPersonalWorkersApi
{
    private static async Task<AiRequestLog> BeginAudit(HelperDb db, ClaimsPrincipal principal, string operation, int owner)
    {
        int actor = await db.Users.Where(u => u.Username == principal.Identity!.Name).Select(u => u.Id).SingleAsync();
        var audit = new AiRequestLog { Id = Guid.NewGuid().ToString("N"), OwnerUserId = actor, Backend = "WorkerManagement", ModelId = operation + ":" + owner, Status = "Failed" };
        db.AiRequestLogs.Add(audit); await db.SaveChangesAsync(); return audit;
    }
    public static void MapPersonalWorkers(this RouteGroupBuilder api)
    {
        api.MapGet("/settings/workers", (AiSettingsStore store) => Results.Ok(store.WorkerViews())).RequireAuthorization("ai.settings.manage");
        api.MapGet("/settings/workers/{owner:int}/runtime", async (int owner, AiSettingsStore store, AiWorkerLifecycle lifecycle, CancellationToken token) =>
            Results.Ok(await lifecycle.StatusAsync(store.WorkerRoute(owner), token))).RequireAuthorization("ai.settings.manage");
        api.MapPost("/settings/workers/{owner:int}/stop", async (int owner, HelperDb db, ClaimsPrincipal p, AiSettingsStore store, AiWorkerLifecycle lifecycle, AiGate gate, CancellationToken token) =>
        {
            await gate.Mutations.WaitAsync(token);
            try
            {
                if (await db.AiRequestLogs.AnyAsync(r => r.OwnerUserId == owner && r.Status == "Running", token) ||
                    await db.AiSessions.AnyAsync(s => s.OwnerUserId == owner && s.Status == "Creating", token))
                    return Results.Json(new { error = "Worker has an active operation" }, statusCode: 409);
                var audit = await BeginAudit(db, p, "stop", owner);
                var status = await lifecycle.StopAsync(store.WorkerRoute(owner), token);
                audit.Status = "Stopped"; await db.SaveChangesAsync(CancellationToken.None);
                return Results.Ok(status);
            }
            finally { gate.Mutations.Release(); }
        }).RequireAuthorization("ai.settings.manage");
        api.MapPost("/settings/workers", async (AiWorkerRegistration request, HelperDb db, ClaimsPrincipal p, AiSettingsStore store, AiGate gate) =>
        {
            if (!await db.Users.AnyAsync(u => u.Id == request.OwnerUserId && u.IsActive)) throw new ArgumentException("Active user not found");
            await gate.Mutations.WaitAsync();
            try { var audit = await BeginAudit(db, p, "assign", request.OwnerUserId); store.RegisterWorker(request); audit.Status = "Assigned"; await db.SaveChangesAsync(); return Results.Ok(new { assigned = true }); }
            finally { gate.Mutations.Release(); }
        }).RequireAuthorization("ai.settings.manage");
        api.MapPut("/settings/workers/{owner:int}/enabled", async (int owner, bool enabled, HelperDb db, ClaimsPrincipal p, AiSettingsStore store, AiGate gate) =>
        {
            await gate.Mutations.WaitAsync();
            try { var audit = await BeginAudit(db, p, "enabled", owner); store.ChangeWorker(owner, enabled: enabled); audit.Status = enabled ? "Enabled" : "Disabled"; await db.SaveChangesAsync(); return Results.Ok(new { enabled }); }
            finally { gate.Mutations.Release(); }
        }).RequireAuthorization("ai.settings.manage");
        api.MapGet("/credentials", async (HelperDb db, ClaimsPrincipal p, AiSettingsStore store) =>
        {
            var user = await AiApi.EitherUserAsync(db, p); var options = store.Read(); var worker = store.Worker(user.Id);
            return Results.Ok(new { available = AiAccess.PilotEligible(options, user) && options.Backend == "OpenCode" && options.OpenCodeMode == "Personal" && worker is { Enabled: true },
                assigned = worker is not null, configured = worker?.CredentialReady ?? false, provider = worker?.Provider ?? "" });
        });
        api.MapPut("/credentials", async (AiPersonalKey request, HelperDb db, ClaimsPrincipal p, AiSettingsStore store, AiGate gate, OpenCodeBackend backend, CancellationToken token) =>
        {
            var user = await AiApi.EitherUserAsync(db, p, token);
            if (request.ApiKey is null || request.ApiKey.Trim().Length is < 1 or > 4096 || request.ApiKey.Any(char.IsControl)) throw new ArgumentException("Invalid provider key");
            await gate.Mutations.WaitAsync(token);
            try
            {
                user = await AiApi.EitherUserAsync(db, p, token);
                var global = store.Read(); var worker = store.Worker(user.Id);
                if (!AiAccess.PilotEligible(global, user)) throw new UnauthorizedAccessException("Personal AI access is restricted to pilot users");
                if (global.Backend != "OpenCode" || global.OpenCodeMode != "Personal" || worker is not { Enabled: true }) throw new ArgumentException("Personal worker is unavailable");
                if (await db.AiSessions.AnyAsync(s => s.OwnerUserId == user.Id && (s.Status == "Active" || s.Status == "Creating" || s.Status == "Closing" || s.Status == "DeletePending"), token) ||
                    await db.AiRequestLogs.AnyAsync(r => r.OwnerUserId == user.Id && r.Status == "Running", token))
                    return Results.Json(new { error = "Close all conversations and wait for cleanup before changing the key" }, statusCode: 409);
                // Disable first: failures must never silently use the old credential.
                store.ChangeWorker(user.Id, ready: false);
                var audit = new AiRequestLog { Id = Guid.NewGuid().ToString("N"), OwnerUserId = user.Id, Backend = "Credentials", ModelId = "set:" + worker.Provider, Status = "Failed" };
                db.AiRequestLogs.Add(audit); await db.SaveChangesAsync(CancellationToken.None);
                var options = global with { WorkerOwnerId = user.Id, OpenCodeUrl = worker.Url, OpenCodePassword = worker.Password, ApiKey = request.ApiKey.Trim() };
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(100));
                string session = await backend.CreateAsync(options, timeout.Token);
                try { await backend.ConfigureCredentialAsync(options, worker.Provider, timeout.Token); }
                finally { using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await backend.CloseAsync(options, session, true, cleanup.Token); }
                await AiApi.EitherUserAsync(db, p, token);
                if (store.Read().Version != global.Version) throw new ArgumentException("Settings changed during key setup; retry after checking configuration");
                store.ChangeWorker(user.Id, ready: true);
                audit.Status = "Configured"; await db.SaveChangesAsync(CancellationToken.None);
                return Results.Ok(new { configured = true });
            }
            finally { gate.Mutations.Release(); }
        });
        api.MapDelete("/credentials", async (HelperDb db, ClaimsPrincipal p, AiSettingsStore store, AiGate gate, OpenCodeBackend backend, CancellationToken token) =>
        {
            var user = await AiApi.EitherUserAsync(db, p, token);
            await gate.Mutations.WaitAsync(token);
            try
            {
                var worker = store.Worker(user.Id) ?? throw new ArgumentException("Personal worker is not assigned");
                store.ChangeWorker(user.Id, ready: false);
                var audit = new AiRequestLog { Id = Guid.NewGuid().ToString("N"), OwnerUserId = user.Id, Backend = "Credentials", ModelId = "delete:" + worker.Provider, Status = "RemoteDeletionPending" };
                db.AiRequestLogs.Add(audit);
                var sessions = await db.AiSessions.Where(s => s.OwnerUserId == user.Id && s.Backend == "OpenCode" && (s.Status == "Active" || s.Status == "Creating" || s.Status == "Closing")).ToListAsync(token);
                sessions = sessions.Where(s => store.RestoreSnapshot(s.ProtectedOptions).OpenCodeUrl == worker.Url).ToList();
                foreach (var session in sessions) { gate.Cancel(session.Id); session.Status = "Closing"; }
                gate.Cancel("diag-user:" + user.Id); await db.SaveChangesAsync(CancellationToken.None);
                bool removed = false;
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(100));
                    foreach (var session in sessions)
                        await backend.CloseAsync(store.RestoreSnapshot(session.ProtectedOptions), session.ExternalSessionId, false, timeout.Token);
                    await backend.RemoveCredentialAsync(new AiOptions { WorkerOwnerId = user.Id, OpenCodeUrl = worker.Url, OpenCodePassword = worker.Password }, worker.Provider, timeout.Token);
                    removed = true;
                }
                catch { /* Revocation remains effective in the gateway; owner can retry remote deletion. */ }
                if (removed) { audit.Status = "Removed"; await db.SaveChangesAsync(CancellationToken.None); }
                return Results.Ok(new { configured = false, removed });
            }
            finally { gate.Mutations.Release(); }
        });
    }
}
