using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LitleHelperServer;

public static class IntegrationApi
{
    public static void MapIntegrationApi(this WebApplication app)
    {
        app.MapGet("/api/settings/glpi-import", (IntegrationSettings settings,GlpiImportHealth health)=>Results.Ok(new { settings=settings.GlpiImport(),health.LastPollAt,health.LastError,health.LastId,health.Initialized })).RequireAuthorization("settings.manage");
        app.MapGet("/api/settings/glpi-import/locations",async(GlpiService glpi,CancellationToken token)=>await glpi.ImportLocationsAsync(token)).RequireAuthorization("settings.manage");
        app.MapPut("/api/settings/glpi-import",async(GlpiImportOptions request,IntegrationSettings settings,HelperDb db,GlpiTicketImport importer,CancellationToken token)=>
        {
            if(request.LocationBranches!=null && await db.Branches.CountAsync(b=>request.LocationBranches.Values.Contains(b.Id)&&b.IsActive)!=request.LocationBranches.Values.Distinct().Count())throw new ArgumentException("Выберите действующие филиалы.");
            bool enabling=request.Enabled&&!settings.GlpiImport().Enabled;settings.SaveGlpiImport(request);if(enabling)await importer.PollAsync(token);return Results.Ok(settings.GlpiImport());
        }).RequireAuthorization("settings.manage");
        app.MapPost("/api/settings/glpi-import/check",async(GlpiTicketImport importer,CancellationToken token)=>Results.Ok(new { imported=await importer.PollAsync(token) })).RequireAuthorization("settings.manage");
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
            PanelUser user;
            try { user = await Messenger.ResolveAdUserAsync(identity, db, token); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
            return Results.Ok(new { token = Security.Token(user, config), user });
        }).RequireRateLimiting("login");
        app.MapGet("/api/settings/telegram", async (IntegrationSettings settings, HelperDb db, TelegramBotHealth health) => Results.Ok(new
        {
            settings = settings.TelegramView(),
            botLastError = health.LastError,
            botLastPollAt = health.LastPollAt,
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
        app.MapPost("/api/settings/telegram/directory/test", async (TelegramDirectoryProbe request, TelegramBotHandler handler, CancellationToken token) =>
        {
            var actor = await handler.ActorAsync(request.TelegramId, token);
            return Results.Ok(new { success = true, message = "AD, права панели и GLPI проверены: " + actor.User.Username, actor.GlpiUserId });
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

        app.MapGet("/api/settings/cartridge", (IntegrationSettings settings) => Results.Ok(settings.Cartridge())).RequireAuthorization("settings.manage");
        app.MapPut("/api/settings/cartridge", (CartridgeOptions update, IntegrationSettings settings) =>
        {
            settings.SaveCartridge(update);
            return Results.Ok(settings.Cartridge());
        }).RequireAuthorization("settings.manage");

        app.MapPost("/api/integrations/cartridges/ready", async (HttpRequest request, Microsoft.AspNetCore.SignalR.IHubContext<HelperHub> hub, IntegrationSettings settings) =>
        {
            var opts = settings.Cartridge();
            if (!opts.Enabled) return Results.Conflict(new { error = "Интеграция с сервисом картриджей отключена" });
            if (!string.IsNullOrWhiteSpace(opts.ApiKey))
            {
                string? providedKey = request.Headers["X-Api-Key"].FirstOrDefault()
                    ?? (request.Headers.Authorization.ToString().StartsWith("Bearer ") ? request.Headers.Authorization.ToString()["Bearer ".Length..] : null);
                if (providedKey != opts.ApiKey) return Results.Unauthorized();
            }

            using var reader = new StreamReader(request.Body);
            string bodyText = await reader.ReadToEndAsync();
            var jsonDoc = System.Text.Json.JsonDocument.Parse(bodyText);
            var list = new List<CartridgeReadyRequest>();
            if (jsonDoc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var el in jsonDoc.RootElement.EnumerateArray())
                    list.Add(System.Text.Json.JsonSerializer.Deserialize<CartridgeReadyRequest>(el.GetRawText(), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!);
            }
            else if (jsonDoc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                list.Add(System.Text.Json.JsonSerializer.Deserialize<CartridgeReadyRequest>(bodyText, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!);
            }

            int count = 0;
            foreach (var item in list)
            {
                if (string.IsNullOrWhiteSpace(item.Username)) continue;
                var notice = new CartridgeReadyNotice(item.Username, item.Marker ?? "", item.Model ?? "", item.Cabinet ?? "", item.ItOffice ?? "Кабинет IT", item.Message ?? "");
                string cleanUser = Security.TicketUser(item.Username).ToLowerInvariant();
                await hub.Clients.Group("AgentUser:" + cleanUser).SendAsync("CartridgeReadyNotice", notice);
                count++;
            }
            return Results.Ok(new { success = true, notified = count });
        });

        app.MapGet("/api/settings/emergency", (IntegrationSettings settings) => Results.Ok(settings.Emergency())).RequireAuthorization("settings.manage");
        app.MapPut("/api/settings/emergency", (EmergencyOptions update, IntegrationSettings settings) =>
        {
            settings.SaveEmergency(update);
            return Results.Ok(settings.Emergency());
        }).RequireAuthorization("settings.manage");

        app.MapPost("/api/settings/emergency/test", async (Microsoft.AspNetCore.SignalR.IHubContext<HelperHub> hub, TelegramClient telegram, System.Security.Claims.ClaimsPrincipal user) =>
        {
            var notice = new EmergencyAlertNotice("CODE_YELLOW", "ТЕСТ: КОД ЖЁЛТЫЙ (Проверка оповещения)", "Кабинет IT", "Тестовая проверка системы экстренного оповещения Pixel Little Helper.", null, null, null, 15, null, null);
            await hub.Clients.Group("AllAgents").SendAsync("EmergencyAlertNotice", notice);
            _ = telegram.SendEmergencyAlertAsync("ТЕСТ: КОД ЖЁЛТЫЙ (Проверка оповещения)", "CODE_YELLOW", "Кабинет IT", null, user.Identity?.Name ?? "Admin", "Тестовая проверка системы экстренного оповещения", null);
            return Results.Ok(new { success = true, message = "Тестовое оповещение отправлено на все подключённые компьютеры и в Telegram" });
        }).RequireAuthorization("settings.manage");

        app.MapPost("/api/integrations/emergency/alert", async (EmergencyAlertRequest req, Microsoft.AspNetCore.SignalR.IHubContext<HelperHub> hub, IntegrationSettings settings, HelperDb db, TelegramClient telegram, System.Security.Claims.ClaimsPrincipal user, IHttpClientFactory httpFactory) =>
        {
            var em = settings.Emergency();
            string upperCode = req.Code.Trim().ToUpperInvariant();
            string title = req.Title ?? upperCode switch
            {
                "CODE_RED" => "КОД КРАСНЫЙ: Пожар / Задымление",
                "CODE_BLACK" => "КОД ЧЁРНЫЙ: Угроза взрыва / Теракт",
                "CODE_ORANGE" => "КОД ОРАНЖЕВЫЙ: ЧС / Опасные вещества",
                "CODE_YELLOW" => "КОД ЖЁЛТЫЙ: Чрезвычайная ситуация",
                "CODE_BLUE" => "КОД СИНИЙ: Реанимация / Остановка сердца",
                "CODE_WHITE" => "КОД БЕЛЫЙ: Агрессия / Нападение",
                "CODE_PINK" => "КОД РОЗОВЫЙ: Потеря / Похищение ребёнка",
                _ => "ЭКСТРЕННОЕ ОПОВЕЩЕНИЕ"
            };
            int duration = req.DurationSeconds ?? (upperCode is "CODE_BLUE" or "CODE_WHITE" ? 180 : 300);
            var notice = new EmergencyAlertNotice(upperCode, title, req.Cabinet, req.Notes, req.Subcode, req.ImageBase64, req.ImageUrl, duration, req.CallId, req.Department);

            if (req.CallId != null && !string.IsNullOrWhiteSpace(req.Department))
            {
                var dept = em.Departments?.FirstOrDefault(d => d.Name.Equals(req.Department, StringComparison.OrdinalIgnoreCase) || d.Id.ToString() == req.Department);
                if (dept != null && dept.ResponsibleUsers.Count > 0)
                {
                    foreach (var u in dept.ResponsibleUsers)
                        await hub.Clients.Group("AgentUser:" + u.Trim().ToLowerInvariant()).SendAsync("EmergencyAlertNotice", notice);
                }
                else
                {
                    await hub.Clients.Group("AllAgents").SendAsync("EmergencyAlertNotice", notice);
                }
            }
            else
            {
                await hub.Clients.Group("AllAgents").SendAsync("EmergencyAlertNotice", notice);
            }

            string operatorName = user.Identity?.Name ?? "External API";
            _ = telegram.SendEmergencyAlertAsync(title, upperCode, req.Cabinet ?? "Не указан", null, operatorName, req.Notes?.Trim(), req.Department);

            db.AuditLogs.Add(new() { AdminUsername = operatorName, MachineName = req.Cabinet ?? "Server", CommandType = "emergency_broadcast", CommandPayload = upperCode + " / " + req.Cabinet, Status = "Completed", Result = "Оповещение запущено" });
            await db.SaveChangesAsync();

            if (em.Enabled && !string.IsNullOrWhiteSpace(em.ServerUrl))
            {
                try
                {
                    var http = httpFactory.CreateClient();
                    http.Timeout = TimeSpan.FromSeconds(3);
                    var endpoint = em.ServerUrl.TrimEnd('/') + "/api/broadcast/client/alert";
                    var body = new { code = upperCode, cabinet = req.Cabinet, notes = req.Notes, client_name = operatorName, image_base64 = req.ImageBase64, image_url = req.ImageUrl };
                    using var alertReq = new HttpRequestMessage(HttpMethod.Post, endpoint);
                    if (!string.IsNullOrWhiteSpace(em.ApiKey)) alertReq.Headers.Add("X-Client-Key", em.ApiKey);
                    alertReq.Content = System.Net.Http.Json.JsonContent.Create(body);
                    _ = http.SendAsync(alertReq);
                }
                catch { }
            }

            return Results.Ok(new { success = true });
        }).RequireAuthorization("commands.execute");
    }
    public record UpdateSwitch(bool Enabled);
    public record TelegramDirectoryProbe(long TelegramId);
}

