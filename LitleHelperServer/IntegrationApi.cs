using Microsoft.EntityFrameworkCore;

namespace LitleHelperServer;

public static class IntegrationApi
{
    public static void MapIntegrationApi(this WebApplication app)
    {
        app.MapGet("/api/settings/updates", (ClientReleases releases) => Results.Ok(new { enabled = releases.Enabled, latest = releases.Latest() })).RequireAuthorization("updates.manage");
        app.MapPut("/api/settings/updates", (UpdateSwitch request, ClientReleases releases) => { releases.SetEnabled(request.Enabled); return Results.Ok(new { enabled = releases.Enabled, latest = releases.Latest() }); }).RequireAuthorization("updates.manage");
        app.MapPost("/api/settings/updates/upload", async (HttpRequest request, ClientReleases releases, CancellationToken token) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "Выберите MSI и подписанный JSON манифеста." });
            var form = await request.ReadFormAsync(token); var msi = form.Files.GetFile("package"); var json = form.Files.GetFile("manifest");
            if (msi == null || json == null) return Results.BadRequest(new { error = "Нужны MSI и JSON манифеста." });
            await releases.PublishAsync(msi, json, token); return Results.Ok(new { enabled = releases.Enabled, latest = releases.Latest() });
        }).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(PixelHelper.Updates.ClientUpdateManifest.MaximumSize + 65536)).RequireAuthorization("updates.manage");
        app.MapGet("/api/client-updates/latest", (ClientReleases releases) => releases.Enabled && releases.Latest() is { } latest ? Results.Ok(latest) : Results.NoContent());
        app.MapGet("/api/client-updates/package/{hash}", (string hash, ClientReleases releases) =>
        {
            var latest = releases.Latest();
            return releases.Enabled && latest?.Sha256 == hash && File.Exists(releases.Package(latest)) ? Results.File(releases.Package(latest), "application/octet-stream", "PixelHelper.msi", enableRangeProcessing: true) : Results.NotFound();
        });
        app.MapGet("/api/auth/providers", (IntegrationSettings settings) => Results.Ok(new { adEnabled = settings.Ad().Enabled }));
        app.MapPost("/api/auth/ad", async (LoginRequest request, IAdAuthentication ad, HelperDb db, IConfiguration config, CancellationToken token) =>
        {
            AdIdentity identity;
            try { identity = await ad.AuthenticateAsync(request, token); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
            var user = await db.Users.SingleOrDefaultAsync(x => x.Username == identity.Username, token);
            if (user != null && (user.AuthSource != "AD" || !user.IsActive)) return Results.Unauthorized();
            if (user == null)
            {
                user = new PanelUser { Username = identity.Username, FullName = identity.FullName, PasswordHash = "!AD", Role = Roles.User };
                db.Users.Add(user);
                try { await db.SaveChangesAsync(token); }
                catch (DbUpdateException)
                {
                    db.Entry(user).State = EntityState.Detached;
                    user = await db.Users.SingleOrDefaultAsync(x => x.Username == identity.Username, token);
                    if (user == null || user.AuthSource != "AD" || !user.IsActive) return Results.Unauthorized();
                }
            }
            return Results.Ok(new { token = Security.Token(user, config), user });
        }).RequireRateLimiting("login");
        app.MapGet("/api/settings/telegram", async (IntegrationSettings settings, HelperDb db) => Results.Ok(new
        {
            settings = settings.TelegramView(),
            pending = await db.TelegramDeliveries.CountAsync(x => x.State == "Pending"),
            failed = await db.TelegramDeliveries.CountAsync(x => x.State == "Failed"),
            lastError = await db.TelegramDeliveries.Where(x => x.LastError != "").OrderByDescending(x => x.TicketId).Select(x => x.LastError).FirstOrDefaultAsync()
        })).RequireAuthorization("settings.manage");
        app.MapPut("/api/settings/telegram", (TelegramUpdate request, IntegrationSettings settings) => { settings.SaveTelegram(request); return Results.Ok(settings.TelegramView()); }).RequireAuthorization("settings.manage");
        app.MapPost("/api/settings/telegram/test", async (TelegramClient telegram, CancellationToken token) =>
        {
            try { await telegram.SendAsync("Проверка Telegram: подключение Pixel Little Helper работает.", token); return Results.Ok(new { success = true, message = "Тестовое сообщение отправлено в группу." }); }
            catch (InvalidOperationException ex) { return Results.Ok(new { success = false, message = ex.Message }); }
        }).RequireAuthorization("settings.manage");
        app.MapPost("/api/settings/telegram/retry", async (HelperDb db) =>
        {
            int count = await db.TelegramDeliveries.Where(x => x.State == "Failed").ExecuteUpdateAsync(x => x.SetProperty(d => d.State, "Pending").SetProperty(d => d.Attempts, 0).SetProperty(d => d.NextAttemptAt, DateTime.UtcNow));
            return Results.Ok(new { count });
        }).RequireAuthorization("settings.manage");
        app.MapGet("/api/settings/ad", (IntegrationSettings settings) => Results.Ok(settings.Ad())).RequireAuthorization("settings.manage");
        app.MapPut("/api/settings/ad", async (AdOptions update, IntegrationSettings settings, HelperDb db, PanelSessions sessions) =>
        {
            settings.SaveAd(update);
            var users = await db.Users.Where(x => x.PasswordHash == "!AD").ToListAsync();
            foreach (var user in users) { user.SecurityVersion++; sessions.Revoke(user.Id); }
            await db.SaveChangesAsync(); return Results.Ok(settings.Ad());
        }).RequireAuthorization("settings.manage");
        app.MapPost("/api/settings/ad/test", async (IAdAuthentication ad, CancellationToken token) =>
        {
            try { await ad.TestConnectionAsync(token); return Results.Ok(new { success = true, message = "LDAPS и сертификат проверены. Вход пользователя проверяется на странице авторизации." }); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return Results.Ok(new { success = false, message = ex.Message }); }
        }).RequireAuthorization("settings.manage");
    }
    public record UpdateSwitch(bool Enabled);
}
