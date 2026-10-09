using Microsoft.AspNetCore.SignalR;
using System.Net.Http.Json;
using System.Text.Json;

namespace LitleHelperServer;

public class EmergencySyncWorker(
    IntegrationSettings settings,
    IHttpClientFactory httpFactory,
    IHubContext<HelperHub> hub,
    ILogger<EmergencySyncWorker> logger,
    TelegramClient? telegram = null) : BackgroundService
{
    private string lastSeenAlarmId = "";
    private int lastSeenCallId = 0;

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var em = settings.Emergency();
            if (!em.Enabled || string.IsNullOrWhiteSpace(em.ServerUrl))
            {
                try { await Task.Delay(TimeSpan.FromSeconds(5), token); } catch (OperationCanceledException) { break; }
                continue;
            }

            try
            {
                var http = httpFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(3);
                var endpoint = em.ServerUrl.TrimEnd('/') + "/api/clients/heartbeat";
                var body = new
                {
                    client_id = "little-helper-bridge",
                    auth_token = em.ApiKey,
                    cabinet = "Сервер помощника"
                };

                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                if (!string.IsNullOrWhiteSpace(em.ApiKey))
                {
                    req.Headers.Add("X-Client-Key", em.ApiKey);
                    req.Headers.Add("X-Auth-Token", em.ApiKey);
                }
                req.Content = JsonContent.Create(body);

                using var response = await http.SendAsync(req, token);
                if (response.IsSuccessStatusCode)
                {
                    using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("active_broadcast", out var b) && b.ValueKind == JsonValueKind.Object)
                    {
                        string alarmId = b.TryGetProperty("alarm_id", out var aId) ? aId.ToString() : "";
                        if (!string.IsNullOrEmpty(alarmId) && alarmId != lastSeenAlarmId)
                        {
                            lastSeenAlarmId = alarmId;
                            string code = b.TryGetProperty("code", out var cVal) ? (cVal.GetString() ?? "CODE_RED") : "CODE_RED";
                            string title = b.TryGetProperty("title", out var tVal) ? (tVal.GetString() ?? "ТРЕВОГА") : "ТРЕВОГА";
                            string cabinet = b.TryGetProperty("cabinet", out var cabVal) ? (cabVal.GetString() ?? "") : "";
                            var notice = new EmergencyAlertNotice(code, title, cabinet, null, null, null, null, 300, null, null);
                            await hub.Clients.Group("AllAgents").SendAsync("EmergencyAlertNotice", notice, token);
                            if (telegram != null)
                            {
                                _ = telegram.SendEmergencyAlertAsync(title, code, cabinet, null, "AudioRONGTA (ROXTON SOS)", null, null, token);
                            }
                            logger.LogInformation("Broadcasted emergency alert from AudioRONGTA: {Code} ({Title})", code, title);
                        }
                    }
                    else
                    {
                        lastSeenAlarmId = "";
                    }

                    if (root.TryGetProperty("active_specialist_call", out var call) && call.ValueKind == JsonValueKind.Object)
                    {
                        int callId = call.TryGetProperty("id", out var idVal) ? idVal.GetInt32() : 0;
                        if (callId > 0 && callId != lastSeenCallId)
                        {
                            lastSeenCallId = callId;
                            string cabinet = call.TryGetProperty("cabinet", out var cab) ? (cab.GetString() ?? "") : "";
                            int deptId = call.TryGetProperty("department_id", out var dId) ? dId.GetInt32() : 0;
                            var dept = em.Departments?.FirstOrDefault(d => d.Id == deptId);
                            string deptName = dept?.Name ?? "Дежурная служба";
                            var notice = new EmergencyAlertNotice(
                                "CODE_BLUE",
                                $"Срочный вызов: {deptName} (Каб. {cabinet})",
                                cabinet,
                                $"Требуется специалист отделения «{deptName}» в кабинет {cabinet}.",
                                null,
                                null,
                                null,
                                180,
                                callId,
                                deptName
                            );

                            if (dept != null && dept.ResponsibleUsers.Count > 0)
                            {
                                foreach (var user in dept.ResponsibleUsers)
                                    await hub.Clients.Group("AgentUser:" + user.Trim().ToLowerInvariant()).SendAsync("EmergencyAlertNotice", notice, token);
                            }
                            else
                            {
                                await hub.Clients.Group("AllAgents").SendAsync("EmergencyAlertNotice", notice, token);
                            }
                            if (telegram != null)
                            {
                                _ = telegram.SendEmergencyAlertAsync(notice.Title, "CODE_BLUE", cabinet, null, "AudioRONGTA (ROXTON SOS)", notice.Notes, deptName, token);
                            }
                            logger.LogInformation("Broadcasted specialist call from AudioRONGTA: CallId={CallId}, Dept={Dept}", callId, deptName);
                        }
                    }
                    else
                    {
                        lastSeenCallId = 0;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogDebug("AudioRONGTA sync poll failed: {Message}", ex.Message);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(3), token); } catch (OperationCanceledException) { break; }
        }
    }
}
