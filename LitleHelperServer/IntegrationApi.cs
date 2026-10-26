using System.Text.Json;
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
        app.MapGet("/api/settings/ad", (IntegrationSettings settings) => Results.Ok(settings.AdView())).RequireAuthorization("settings.manage");
        app.MapPut("/api/settings/ad", async (AdUpdate update, IntegrationSettings settings, HelperDb db, PanelSessions sessions) =>
        {
            settings.SaveAd(update);
            var users = await db.Users.Where(x => x.PasswordHash == "!AD").ToListAsync();
            foreach (var user in users) { user.SecurityVersion++; sessions.Revoke(user.Id); }
            await db.SaveChangesAsync(); return Results.Ok(settings.AdView());
        }).RequireAuthorization("settings.manage");
        app.MapPost("/api/settings/ad/test", async (IAdAuthentication ad, CancellationToken token) =>
        {
            try { await ad.TestConnectionAsync(token); return Results.Ok(new { success = true, message = "LDAPS и сертификат проверены. Вход пользователя проверяется на странице авторизации." }); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return Results.Ok(new { success = false, message = ex.Message }); }
        }).RequireAuthorization("settings.manage");
        app.MapPost("/api/ad/change-password", async (ChangeAdPasswordRequest request, AdPasswordService adPasswordService, HelperDb db, System.Security.Claims.ClaimsPrincipal actor, CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.NewPassword))
                return Results.BadRequest(new { success = false, message = "Не указан логин или пароль." });

            if(request.Username.Length>256 || request.NewPassword.Length>256 || request.CurrentPassword.Length>256)
                return Results.BadRequest(new {success=false,message="Слишком длинный логин или пароль."});
            var result = await adPasswordService.ChangePasswordAsync(request.Username, request.NewPassword, request.CurrentPassword,token);
            db.AuditLogs.Add(new() {AdminUsername=actor.Identity?.Name??"",CommandType="ad_self_password_change",CommandPayload=Security.Login(request.Username),Status=result.Success?"Completed":"Failed",Result=result.Message});
            await db.SaveChangesAsync(token);
            if (!result.Success)
            {
                return Results.BadRequest(new { success = false, message = result.Message });
            }
            return Results.Ok(new { success = true, message = result.Message });
        }).RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute { AuthenticationSchemes = "Bearer,Agent" }).RequireRateLimiting("login");

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
            if (string.IsNullOrWhiteSpace(opts.ApiKey))
                return Results.Problem("Не настроен ключ интеграции.", statusCode: 503);
            {
                string? providedKey = request.Headers["X-Api-Key"].FirstOrDefault()
                    ?? (request.Headers.Authorization.ToString().StartsWith("Bearer ") ? request.Headers.Authorization.ToString()["Bearer ".Length..] : null);
                if (providedKey == null || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(providedKey), System.Text.Encoding.UTF8.GetBytes(opts.ApiKey))) return Results.Unauthorized();
            }

            using var reader = new StreamReader(request.Body);
            string bodyText = await reader.ReadToEndAsync();
            using var jsonDoc = System.Text.Json.JsonDocument.Parse(bodyText);
            var rawItems = new List<System.Text.Json.JsonElement>();

            if (jsonDoc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var el in jsonDoc.RootElement.EnumerateArray()) rawItems.Add(el);
            }
            else if (jsonDoc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (jsonDoc.RootElement.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var el in itemsEl.EnumerateArray()) rawItems.Add(el);
                }
                else
                {
                    rawItems.Add(jsonDoc.RootElement);
                }
            }

            if(rawItems.Count>1000 || rawItems.Any(el=>el.ValueKind!=JsonValueKind.Object))
                return Results.BadRequest(new {error="Допустимо не более 1000 уведомлений-объектов."});
            int count = 0;
            foreach (var el in rawItems)
            {
                string user = "";
                if (el.TryGetProperty("targetUser", out var tu)) user = tu.GetString() ?? "";
                else if (el.TryGetProperty("target_user", out var tu2)) user = tu2.GetString() ?? "";
                else if (el.TryGetProperty("username", out var u)) user = u.GetString() ?? "";
                else if (el.TryGetProperty("login", out var lg)) user = lg.GetString() ?? "";

                if (string.IsNullOrWhiteSpace(user)) continue;

                string marker = el.TryGetProperty("marker", out var m) ? (m.GetString() ?? "") : "";
                string model = el.TryGetProperty("model", out var mdl) ? (mdl.GetString() ?? "") : "";
                string cabinet = el.TryGetProperty("cabinet", out var c) ? (c.GetString() ?? "") : "";
                string office = "";
                if (el.TryGetProperty("office", out var ofc)) office = ofc.GetString() ?? "";
                else if (el.TryGetProperty("itOffice", out var ofc2)) office = ofc2.GetString() ?? "";
                else if (el.TryGetProperty("it_office", out var ofc3)) office = ofc3.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(office)) office = "Кабинет IT";

                string message = el.TryGetProperty("message", out var msg) ? (msg.GetString() ?? "") : "";
                string displayName = "";
                if (el.TryGetProperty("userName", out var un)) displayName = un.GetString() ?? "";
                else if (el.TryGetProperty("user_name", out var un2)) displayName = un2.GetString() ?? "";
                else if (el.TryGetProperty("displayName", out var un3)) displayName = un3.GetString() ?? "";
                string displayOrUser = !string.IsNullOrWhiteSpace(displayName) ? displayName : user;

                var notice = new CartridgeReadyNotice(displayOrUser, marker, model, cabinet, office, message);
                string cleanUser = Security.TicketUser(user).ToLowerInvariant();
                await hub.Clients.Group("AgentUser:" + cleanUser).SendAsync("CartridgeReadyNotice", notice);
                count++;
            }
            return Results.Ok(new { success = true, notified = count });
        }).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(131072)).AllowAnonymous();

        app.MapGet("/api/settings/emergency", (IntegrationSettings settings) => Results.Ok(settings.Emergency())).RequireAuthorization("settings.manage");
        app.MapPut("/api/settings/emergency", async (EmergencyOptions update, IntegrationSettings settings, Microsoft.AspNetCore.SignalR.IHubContext<HelperHub> hub) =>
        {
            settings.SaveEmergency(update);
            await hub.Clients.Group("AllAgents").SendAsync("EmergencySettingsUpdated");
            return Results.Ok(settings.Emergency());
        }).RequireAuthorization("settings.manage");

        app.MapPost("/api/settings/emergency/test", async (Microsoft.AspNetCore.SignalR.IHubContext<HelperHub> hub, TelegramClient telegram, System.Security.Claims.ClaimsPrincipal user) =>
        {
            var notice = new EmergencyAlertNotice("CODE_YELLOW", "ТЕСТ: КОД ЖЁЛТЫЙ (Проверка оповещения)", "Кабинет IT", "Тестовая проверка системы экстренного оповещения Pixel Little Helper.", null, null, null, 15, null, null);
            await hub.Clients.Group("AllAgents").SendAsync("EmergencyAlertNotice", notice);
            await telegram.SendEmergencyAlertAsync("ТЕСТ: КОД ЖЁЛТЫЙ (Проверка оповещения)", "CODE_YELLOW", "Кабинет IT", null, user.Identity?.Name ?? "Admin", "Тестовая проверка системы экстренного оповещения", null);
            return Results.Ok(new { success = true, message = "Тестовое оповещение отправлено на все подключённые компьютеры и в Telegram" });
        }).RequireAuthorization("settings.manage");

        app.MapPost("/api/settings/emergency/cancel", async (IHubContext<HelperHub> hub,IntegrationSettings settings,HelperDb db,System.Security.Claims.ClaimsPrincipal user,IHttpClientFactory factory,CancellationToken token) => {
            await EmergencyEvents.CancelAsync(user,db,settings,hub.Clients,factory,token);
            return Results.Ok(new {success=true,message="Локальный отбой выполнен в доступных филиалах. Внешняя отмена поставлена в очередь; статус доступен в журнале доставки."});
        }).RequireAuthorization("emergency.manage");
        app.MapGet("/api/emergency/deliveries",async(HelperDb db,System.Security.Claims.ClaimsPrincipal principal) => {
            var user=await db.Users.SingleAsync(u=>u.Username==principal.Identity!.Name);
            return Results.Ok(await db.EmergencyEvents.AsNoTracking().Where(e=>user.Role==Roles.SuperAdmin || e.BranchId==user.BranchId)
                .OrderByDescending(e=>e.Id).Take(100).Select(e=>new {e.EventId,e.CreatedAt,e.BranchName,e.LocalDelivered,e.ExternalState,e.ExternalCallId,e.Attempts,e.LastError,e.Canceled,e.TelegramState,e.TelegramError}).ToListAsync());
        }).RequireAuthorization("emergency.manage");

        app.MapPost("/api/settings/emergency/ping", async (EmergencyPingRequest? pingReq, IntegrationSettings settings, IHttpClientFactory httpFactory) =>
        {
            var em = settings.Emergency();
            string targetUrl = (!string.IsNullOrWhiteSpace(pingReq?.ServerUrl) ? pingReq.ServerUrl : em.ServerUrl)?.Trim().TrimEnd('/') ?? "http://172.16.16.63:8085";
            string key = !string.IsNullOrWhiteSpace(pingReq?.ApiKey) ? pingReq.ApiKey.Trim() : em.ApiKey.Trim();

            var http = httpFactory.CreateClient("EmergencyBridge");
            http.Timeout = TimeSpan.FromSeconds(5);

            try
            {
                var endpoint = targetUrl + "/api/clients/heartbeat";
                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                req.Headers.Add("X-Client-Key", key);
                req.Headers.Add("X-Auth-Token", key);
                req.Content = JsonContent.Create(new
                {
                    client_id = "little-helper-bridge",
                    auth_token = key,
                    cabinet = "Сервер помощника"
                });

                using var response = await http.SendAsync(req);
                if (!response.IsSuccessStatusCode)
                {
                    return Results.BadRequest(new { error = $"Сервер AudioRONGTA вернул HTTP {(int)response.StatusCode} {response.ReasonPhrase}" });
                }

                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                var root = doc.RootElement;
                string clientStatus = root.TryGetProperty("client_status", out var cs) ? (cs.GetString() ?? "approved") : "approved";
                bool isBusy = root.TryGetProperty("is_busy", out var ib) && ib.GetBoolean();

                return Results.Ok(new
                {
                    success = true,
                    serverUrl = targetUrl,
                    clientStatus,
                    isBusy,
                    message = $"Связь с AudioRONGTA успешно установлена! Статус допуска клиента: {clientStatus}."
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Не удалось связаться с AudioRONGTA ({targetUrl}): {ex.Message}" });
            }
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
            var notice = new EmergencyAlertNotice(upperCode, title, req.Cabinet, req.Notes, req.Subcode, req.ImageBase64, req.ImageUrl, duration, req.CallId, req.Department, EmergencyLanguages.ForCode(em,upperCode));

            if (!EmergencyLanguages.AllowedForRole(em,user.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value??Roles.User).Contains(upperCode)) return Results.BadRequest(new {error="Код отключён администратором"});
            var actor=await db.Users.AsNoTracking().SingleAsync(u=>u.Username==user.Identity!.Name);
            if(actor.Role!=Roles.SuperAdmin && req.BranchId.HasValue && actor.BranchId!=req.BranchId)return Results.Forbid();
            int? targetBranch=req.BranchId;
            if(targetBranch==null && !em.BroadcastAllBranches)
                targetBranch=(await db.Users.AsNoTracking().FirstOrDefaultAsync(u=>u.Username==user.Identity!.Name))?.BranchId;
            var branch=targetBranch.HasValue?await db.Branches.AsNoTracking().FirstOrDefaultAsync(b=>b.Id==targetBranch && b.IsActive):null;
            if(branch==null && !em.BroadcastAllBranches && upperCode is not ("CODE_WHITE" or "CODE_BLUE" or "SPECIALIST_CALL"))
                return Results.BadRequest(new {error="Выберите филиал для массового оповещения"});
            if(req.BranchId.HasValue && branch==null) return Results.BadRequest(new {error="Филиал не найден"});
            var delivery=await EmergencyEvents.DispatchAsync(notice,branch?.Id,branch?.Name,user.Identity?.Name??"","Server",db,settings,hub.Clients,CancellationToken.None);
            string operatorName = user.Identity?.Name ?? "External API";
            

            db.AuditLogs.Add(new() { AdminUsername = operatorName, MachineName = req.Cabinet ?? "Server", CommandType = "emergency_broadcast", CommandPayload = upperCode + " / " + req.Cabinet, Status = "Completed", Result = "Рассылка передана SignalR; внешний канал: "+delivery.ExternalState });
            await db.SaveChangesAsync();

            return Results.Ok(new {success=true,delivery.EventId,delivery.LocalDelivered,delivery.ExternalState});
        }).RequireAuthorization("commands.execute");

        app.MapGet("/api/guard/white-state", () => Results.Ok(GuardWhiteService.GetState()));

        app.MapPost("/api/guard/white-trigger", async (HttpContext ctx, HelperWhiteTriggerRequest req, IntegrationSettings settings, HelperDb db, Microsoft.AspNetCore.SignalR.IHubContext<HelperHub> hub) =>
        {
            var em = settings.Emergency();
            string? token = ctx.Request.Headers["X-Client-Key"].FirstOrDefault()
                ?? ctx.Request.Headers["X-Auth-Token"].FirstOrDefault()
                ?? (ctx.Request.Headers.TryGetValue("Authorization", out var authVal) && authVal.ToString().StartsWith("Bearer ") ? authVal.ToString()[7..] : null);

            bool authorized = false;
            if (!string.IsNullOrWhiteSpace(em.ApiKey) && !string.IsNullOrWhiteSpace(token) &&
                System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(token.Trim()),
                    System.Text.Encoding.UTF8.GetBytes(em.ApiKey.Trim())))
            {
                authorized = true;
            }
            else if (ctx.User.Identity?.IsAuthenticated == true && (Access.Can(ctx.User, "emergency.manage") || Access.Can(ctx.User, "commands.execute")))
            {
                authorized = true;
            }

            if (!authorized)
                return Results.Unauthorized();

            string eventId = string.IsNullOrWhiteSpace(req.EventId) ? $"emergency-white:{Guid.NewGuid():N}" : req.EventId.Trim();
            string cabinet = string.IsNullOrWhiteSpace(req.Cabinet) ? "Не указан" : req.Cabinet.Trim();
            string branchName = string.IsNullOrWhiteSpace(req.BranchName) ? "Главный корпус" : req.BranchName.Trim();
            string time = string.IsNullOrWhiteSpace(req.Time) ? DateTime.Now.ToString("HH:mm:ss") : req.Time.Trim();

            GuardWhiteService.TriggerAlert(cabinet, branchName, time, eventId);
            await hub.Clients.Group("GuardWhite").SendAsync("CodeWhiteTriggered", new { cabinet, branchName, time, eventId });

            db.AuditLogs.Add(new() {
                AdminUsername = ctx.User.Identity?.Name ?? "EmergencySystem (Bridge)",
                CommandType = "external_white_trigger",
                CommandPayload = $"{cabinet} / {branchName}",
                Status = "Completed",
                Result = "Внешний триггер Код Белый принят и выведен на экран охраны"
            });
            await db.SaveChangesAsync();

            return Results.Ok(new { success = true, eventId, cabinet, branchName });
        });

        app.MapPost("/api/guard/white-ack", async (System.Security.Claims.ClaimsPrincipal user, HelperDb db) => {
            GuardWhiteService.AcknowledgeAlert();
            db.AuditLogs.Add(new() {AdminUsername=user.Identity?.Name??"", CommandType="white_ack", Status="Completed"});
            await db.SaveChangesAsync(); return Results.Ok(new {success=true});
        }).RequireAuthorization("emergency.manage");

        app.MapPost("/api/guard/white-reset", async (HttpContext ctx, Microsoft.AspNetCore.SignalR.IHubContext<HelperHub> hub, HelperDb db, IntegrationSettings settings) =>
        {
            var em = settings.Emergency();
            string? token = ctx.Request.Headers["X-Client-Key"].FirstOrDefault()
                ?? ctx.Request.Headers["X-Auth-Token"].FirstOrDefault()
                ?? (ctx.Request.Headers.TryGetValue("Authorization", out var authVal) && authVal.ToString().StartsWith("Bearer ") ? authVal.ToString()[7..] : null);

            bool authorized = false;
            string actorName = "External Bridge";
            if (!string.IsNullOrWhiteSpace(em.ApiKey) && !string.IsNullOrWhiteSpace(token) &&
                System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(token.Trim()),
                    System.Text.Encoding.UTF8.GetBytes(em.ApiKey.Trim())))
            {
                authorized = true;
                actorName = "EmergencySystem (Bridge)";
            }
            else if (ctx.User.Identity?.IsAuthenticated == true && Access.Can(ctx.User, "emergency.manage"))
            {
                authorized = true;
                actorName = ctx.User.Identity.Name ?? "Operator";
            }

            if (!authorized)
                return Results.Unauthorized();

            string? id = GuardWhiteService.GetState().EventId;
            if (id != null) {
                var entry = await db.EmergencyEvents.SingleOrDefaultAsync(e => e.EventId == id);
                if (entry != null) {
                    entry.Canceled = true;
                    entry.ExternalState = entry.ExternalState == "Disabled" || entry.Attempts == 0 && entry.ExternalState == "Pending" ? "Canceled" : "CancelPending";
                    entry.Attempts = 0;
                    entry.NextAttemptAt = DateTime.UtcNow;
                }
            }
            db.AuditLogs.Add(new() { AdminUsername = actorName, CommandType = "white_reset", Status = "Completed", CommandPayload = id ?? "" });
            await db.SaveChangesAsync();
            GuardWhiteService.ResetAlert(id);
            await hub.Clients.Group("GuardWhite").SendAsync("CodeWhiteReset");
            return Results.Ok(new { success = true });
        });

        app.MapGet("/api/guard/white-events", async (HttpContext ctx, CancellationToken token) =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";

            var initial = GuardWhiteService.GetState();
            await ctx.Response.WriteAsync($"event: state\ndata: {System.Text.Json.JsonSerializer.Serialize(initial)}\n\n", token);
            await ctx.Response.Body.FlushAsync(token);

            var queue=System.Threading.Channels.Channel.CreateBounded<string>(new System.Threading.Channels.BoundedChannelOptions(16) {
                SingleReader=true, FullMode=System.Threading.Channels.BoundedChannelFullMode.DropOldest });
            Action<CodeWhiteAlertData> onAlert=data=>queue.Writer.TryWrite("event: alert\ndata: "+JsonSerializer.Serialize(data)+"\n\n");
            Action onReset=()=>queue.Writer.TryWrite("event: reset\ndata: {}\n\n");
            GuardWhiteService.OnAlert+=onAlert;GuardWhiteService.OnReset+=onReset;
            using var heartbeatStop=CancellationTokenSource.CreateLinkedTokenSource(token);
            var heartbeat=Task.Run(async()=> {
                try {using var timer=new PeriodicTimer(TimeSpan.FromSeconds(15));
                    while(await timer.WaitForNextTickAsync(heartbeatStop.Token))
                        queue.Writer.TryWrite("event: state\ndata: "+JsonSerializer.Serialize(GuardWhiteService.GetState())+"\n\n");
                }catch(OperationCanceledException){}
            });
            try {
                await foreach(var message in queue.Reader.ReadAllAsync(token)) {
                    await ctx.Response.WriteAsync(message,token);
                    await ctx.Response.Body.FlushAsync(token);
                }
            }catch(OperationCanceledException){}
            finally {
                GuardWhiteService.OnAlert-=onAlert;GuardWhiteService.OnReset-=onReset;
                heartbeatStop.Cancel();await heartbeat;
            }
        });

        app.MapGet("/api/internal/resolve-client", async (string? ip, string? machine, HelperDb db, CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(ip) && string.IsNullOrWhiteSpace(machine))
                return Results.BadRequest(new { error = "IP or machine is required" });

            ip = ip?.Trim();
            machine = machine?.Trim();
            var query = db.Computers.AsQueryable();

            if (!string.IsNullOrWhiteSpace(machine))
            {
                string m = machine.ToLower();
                query = query.Where(c => c.MachineName.ToLower() == m);
            }
            else if (!string.IsNullOrWhiteSpace(ip))
            {
                query = query.Where(c => c.IpAddress == ip || c.IpAddress.Contains(ip));
            }

            var computer = await query
                .OrderByDescending(c => c.IsOnline)
                .ThenByDescending(c => c.LastSeen)
                .FirstOrDefaultAsync(token);

            if (computer == null) return Results.NotFound(new { error = "Not found", ip, machine });
            return Results.Ok(new
            {
                ip = computer.IpAddress,
                machineName = computer.MachineName,
                domain = computer.DomainName,
                username = computer.CurrentUser,
                fullName = computer.CurrentUserFullName,
                room = computer.Room,
                branchId = computer.BranchId,
                isOnline = computer.IsOnline,
                lastSeen = computer.LastSeen
            });
        });

        app.MapGet("/api/settings/security", (IntegrationSettings settings, AntivirusSyncWorker avWorker) => Results.Ok(new
        {
            settings = settings.Security(),
            antivirusStatus = avWorker.GetStatus()
        })).RequireAuthorization("settings.manage");

        app.MapPut("/api/settings/security", async (SecurityOptions request, IntegrationSettings settings, IHubContext<HelperHub> hub) =>
        {
            settings.SaveSecurity(request);
            await hub.Clients.Group("AllAgents").SendAsync("WebGuardToggled", request.WebGuardEnabled);
            await hub.Clients.Group("AllAgents").SendAsync("AntivirusSettingsUpdated", JsonSerializer.Serialize(request));
            return Results.Ok(settings.Security());
        }).RequireAuthorization("settings.manage");

        app.MapGet("/api/settings/security/gpo-script", (HttpRequest req) =>
        {
            string host = req.Host.Value;
            if (string.IsNullOrWhiteSpace(host)) host = "helper.gp1.loc";
            string script = WebGuardGpoGenerator.GenerateScript(host);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(script);
            return Results.File(bytes, "application/octet-stream", "Deploy-WebGuardGpo.ps1");
        }).RequireAuthorization("settings.manage");

        app.MapGet("/api/settings/security/antivirus-gpo-script", () =>
        {
            string script = AntivirusGpoGenerator.GenerateScript();
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(script);
            return Results.File(bytes, "application/octet-stream", "Deploy-AntivirusTrustGpo.ps1");
        }).RequireAuthorization("settings.manage");

        app.MapPost("/api/antivirus/sync-now", async (AntivirusSyncWorker avWorker, CancellationToken token) =>
        {
            try
            {
                bool success = await avWorker.TriggerSyncNowAsync(token, force: true);
                return Results.Ok(new { success, message = success ? "Синхронизация баз успешно запущена и завершена." : "Синхронизация уже выполняется." });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        }).RequireAuthorization("settings.manage");

        app.MapPost("/api/antivirus/probe-servers", async (AntivirusSyncWorker avWorker, CancellationToken token) =>
        {
            try
            {
                var probeList = await avWorker.ProbeUpdateServersAsync(token);
                return Results.Ok(probeList);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireAuthorization("settings.manage");

        app.MapGet("/api/antivirus/manifest", (AntivirusSyncWorker avWorker) =>
        {
            var manifest = avWorker.GetManifest();
            return manifest != null ? Results.Ok(manifest) : Results.NoContent();
        }).RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute { AuthenticationSchemes = "Bearer,Agent" });

        app.MapGet("/api/antivirus/bundle", (AntivirusSyncWorker avWorker) =>
        {
            string? bundlePath = avWorker.GetBundlePath();
            return bundlePath != null && File.Exists(bundlePath)
                ? Results.File(bundlePath, "application/zip", "antivirus_bundle.zip", enableRangeProcessing: true)
                : Results.NotFound(new { error = "Пакет баз ещё не сформирован на сервере." });
        }).RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute { AuthenticationSchemes = "Bearer,Agent" });

        app.MapGet("/api/antivirus/threats", async (HelperDb db, string? status, CancellationToken token) =>
        {
            var query = db.ThreatRecords.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && status != "all")
            {
                query = query.Where(t => t.Status == status);
            }
            var list = await query.OrderByDescending(t => t.DetectedAt).Take(200).ToListAsync(token);
            return Results.Ok(list);
        }).RequireAuthorization("settings.manage");

        app.MapPost("/api/antivirus/threats", async (ThreatReportRequest req, HelperDb db, IntegrationSettings settings, IHubContext<HelperHub> hub, CancellationToken token) =>
        {
            var sec = settings.Security();
            DateTime now = DateTime.UtcNow;
            DateTime? until = sec.AntivirusRetentionDays > 0 ? now.AddDays(sec.AntivirusRetentionDays) : null;

            var record = new ThreatRecord
            {
                MachineName = req.MachineName,
                UserName = req.UserName,
                FilePath = req.FilePath,
                ThreatName = req.ThreatName,
                ThreatType = req.ThreatType,
                FileSizeBytes = req.FileSizeBytes,
                Sha256 = req.Sha256,
                Status = "Quarantined",
                DetectedAt = now,
                QuarantinedUntil = until,
                ActionTaken = req.ActionTaken
            };

            db.ThreatRecords.Add(record);
            await db.SaveChangesAsync(token);

            await hub.Clients.Group("PanelStaff").SendAsync("ThreatDetected", record);
            return Results.Ok(record);
        }).RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute { AuthenticationSchemes = "Bearer,Agent" });

        app.MapPost("/api/antivirus/threats/{id:int}/restore", async (int id, HelperDb db, IHubContext<HelperHub> hub, System.Security.Claims.ClaimsPrincipal actor, CancellationToken token) =>
        {
            var threat = await db.ThreatRecords.FindAsync([id], token);
            if (threat == null) return Results.NotFound(new { error = "Запись об угрозе не найдена." });

            threat.Status = "Restored";
            threat.RestoredAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(threat.Sha256))
            {
                bool exists = await db.AntivirusWhitelist.AnyAsync(w => w.Sha256 == threat.Sha256, token);
                if (!exists)
                {
                    db.AntivirusWhitelist.Add(new AntivirusWhitelistItem
                    {
                        Sha256 = threat.Sha256,
                        FilePath = threat.FilePath,
                        AddedBy = actor.Identity?.Name ?? "Admin",
                        Reason = $"Восстановлен из карантина ({threat.ThreatName})",
                        AddedAt = DateTime.UtcNow
                    });
                }
            }

            await db.SaveChangesAsync(token);

            var payload = JsonSerializer.Serialize(new
            {
                threatId = threat.Id,
                filePath = threat.FilePath,
                sha256 = threat.Sha256
            });
            await hub.Clients.Group("AgentMachine:" + threat.MachineName.ToLowerInvariant()).SendAsync("AntivirusRestoreFile", payload);

            return Results.Ok(new { success = true, threat });
        }).RequireAuthorization("settings.manage");

        app.MapDelete("/api/antivirus/threats/{id:int}", async (int id, HelperDb db, IHubContext<HelperHub> hub, CancellationToken token) =>
        {
            var threat = await db.ThreatRecords.FindAsync([id], token);
            if (threat == null) return Results.NotFound(new { error = "Запись об угрозе не найдена." });

            threat.Status = "Deleted";
            threat.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(token);

            var payload = JsonSerializer.Serialize(new
            {
                threatId = threat.Id,
                filePath = threat.FilePath,
                sha256 = threat.Sha256
            });
            await hub.Clients.Group("AgentMachine:" + threat.MachineName.ToLowerInvariant()).SendAsync("AntivirusDeleteFile", payload);

            return Results.Ok(new { success = true, threat });
        }).RequireAuthorization("settings.manage");
    }
    public record UpdateSwitch(bool Enabled);
    public record TelegramDirectoryProbe(long TelegramId);
    public record ThreatReportRequest(string MachineName, string UserName, string FilePath, string ThreatName, string ThreatType, long FileSizeBytes, string Sha256, string ActionTaken = "Quarantined");
    public record ChangeAdPasswordRequest(string Username, string NewPassword, string CurrentPassword = "");
}

