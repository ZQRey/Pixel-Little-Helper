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
        app.MapPost("/api/ad/change-password", async (ChangeAdPasswordRequest request, AdPasswordService adPasswordService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.NewPassword))
                return Results.BadRequest(new { success = false, message = "Не указан логин или пароль." });

            var result = await adPasswordService.ChangePasswordAsync(request.Username, request.NewPassword);
            if (!result.Success)
            {
                return Results.BadRequest(new { success = false, message = result.Message });
            }
            return Results.Ok(new { success = true, message = result.Message });
        }).RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute { AuthenticationSchemes = "Bearer,Agent" });

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

                var notice = new CartridgeReadyNotice(user, marker, model, cabinet, office, message);
                string cleanUser = Security.TicketUser(user).ToLowerInvariant();
                await hub.Clients.Group("AgentUser:" + cleanUser).SendAsync("CartridgeReadyNotice", notice);
                count++;
            }
            return Results.Ok(new { success = true, notified = count });
        }).AllowAnonymous();

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

        app.MapPost("/api/settings/emergency/cancel", async (Microsoft.AspNetCore.SignalR.IHubContext<HelperHub> hub, IntegrationSettings settings, TelegramClient telegram, System.Security.Claims.ClaimsPrincipal user, IHttpClientFactory httpFactory) =>
        {
            await hub.Clients.Group("AllAgents").SendAsync("EmergencyAlertCanceled");
            await hub.Clients.Group("PanelStaff").SendAsync("EmergencyAlertCanceled");

            var em = settings.Emergency();
            var tg = settings.Telegram();
            if (tg.Enabled && em.TelegramAlertsEnabled)
            {
                string targetChat = !string.IsNullOrWhiteSpace(em.TelegramChatId) ? em.TelegramChatId.Trim() : tg.ChatId;
                int targetThread = em.TelegramThreadId > 0 ? em.TelegramThreadId : tg.ThreadId;
                if (!string.IsNullOrWhiteSpace(targetChat))
                {
                    string operatorName = user.Identity?.Name ?? "Администратор";
                    _ = telegram.SendToAsync(targetChat, $"🟢 ОТБОЙ ТРЕВОГИ: Экстренное оповещение сброшено администратором ({operatorName}). Сигналы на всех ПК отключены.", null, CancellationToken.None, targetThread);
                }
            }

            if (em.Enabled && !string.IsNullOrWhiteSpace(em.ServerUrl))
            {
                string effectiveKey = !string.IsNullOrWhiteSpace(em.ApiKey) ? em.ApiKey : "art_helper_bridge_secret";
                var http = httpFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(3);
                foreach (var path in new[] { "/api/broadcast/cancel", "/api/broadcast/abort" })
                {
                    try
                    {
                        var endpoint = em.ServerUrl.TrimEnd('/') + path;
                        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                        req.Headers.Add("X-Client-Key", effectiveKey);
                        req.Headers.Add("X-Auth-Token", effectiveKey);
                        req.Content = JsonContent.Create(new { client_id = "little-helper-bridge", auth_token = effectiveKey });
                        await http.SendAsync(req, CancellationToken.None);
                    }
                    catch { }
                }
            }

            return Results.Ok(new { success = true, message = "Оповещение успешно сброшено (Отбой тревоги отправлен на все ПК)" });
        }).RequireAuthorization("settings.manage");

        app.MapPost("/api/settings/emergency/ping", async (EmergencyPingRequest? pingReq, IntegrationSettings settings, IHttpClientFactory httpFactory) =>
        {
            var em = settings.Emergency();
            string targetUrl = (!string.IsNullOrWhiteSpace(pingReq?.ServerUrl) ? pingReq.ServerUrl : em.ServerUrl)?.Trim().TrimEnd('/') ?? "http://172.16.16.63:8085";
            string key = !string.IsNullOrWhiteSpace(pingReq?.ApiKey) ? pingReq.ApiKey.Trim() : (!string.IsNullOrWhiteSpace(em.ApiKey) ? em.ApiKey.Trim() : "art_helper_bridge_secret");

            var http = httpFactory.CreateClient();
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
            else if (upperCode == "CODE_WHITE")
            {
                GuardWhiteService.TriggerAlert(req.Cabinet ?? "Не указан", "Главный корпус", DateTime.Now.ToString("HH:mm:ss"));
                await hub.Clients.Group("GuardWhite").SendAsync("CodeWhiteAlert", new
                {
                    cabinet = req.Cabinet ?? "Не указан",
                    branchName = "Главный корпус",
                    time = DateTime.Now.ToString("HH:mm:ss")
                });
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

        app.MapGet("/api/guard/white-state", () => Results.Ok(GuardWhiteService.GetState()));

        app.MapPost("/api/guard/white-reset", async (Microsoft.AspNetCore.SignalR.IHubContext<HelperHub> hub) =>
        {
            GuardWhiteService.ResetAlert();
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

            var tcs = new TaskCompletionSource();
            using var reg = token.Register(() => tcs.TrySetResult());

            Action<CodeWhiteAlertData> onAlert = async data =>
            {
                try
                {
                    await ctx.Response.WriteAsync($"event: alert\ndata: {System.Text.Json.JsonSerializer.Serialize(data)}\n\n", CancellationToken.None);
                    await ctx.Response.Body.FlushAsync(CancellationToken.None);
                }
                catch { }
            };

            Action onReset = async () =>
            {
                try
                {
                    await ctx.Response.WriteAsync("event: reset\ndata: {}\n\n", CancellationToken.None);
                    await ctx.Response.Body.FlushAsync(CancellationToken.None);
                }
                catch { }
            };

            GuardWhiteService.OnAlert += onAlert;
            GuardWhiteService.OnReset += onReset;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(15000, token);
                    await ctx.Response.WriteAsync(": keepalive\n\n", token);
                    await ctx.Response.Body.FlushAsync(token);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                GuardWhiteService.OnAlert -= onAlert;
                GuardWhiteService.OnReset -= onReset;
            }
        });
    }
    public record UpdateSwitch(bool Enabled);
    public record TelegramDirectoryProbe(long TelegramId);
    public record ChangeAdPasswordRequest(string Username, string NewPassword);
}

