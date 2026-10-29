using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace LitleHelperServer;

public static class PxeApi
{
    public static void MapPxeApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/pxe").RequireAuthorization("deployment.manage");

        // Settings
        group.MapGet("/settings", (IntegrationSettings settings) =>
        {
            var pxe = settings.Pxe();
            return Results.Ok(new
            {
                enabled = pxe.Enabled,
                serverUrl = pxe.ServerUrl,
                timeoutSeconds = pxe.TimeoutSeconds,
                apiKey = pxe.ApiKey
            });
        });

        group.MapPut("/settings", (PxeSettingsUpdate update, IntegrationSettings settings) =>
        {
            settings.SavePxe(update);
            var pxe = settings.Pxe();
            return Results.Ok(new
            {
                enabled = pxe.Enabled,
                serverUrl = pxe.ServerUrl,
                timeoutSeconds = pxe.TimeoutSeconds,
                apiKey = pxe.ApiKey
            });
        });

        // Test connection
        group.MapPost("/test-connection", async ([FromBody] PxeTestRequest? req, PxeService service, CancellationToken token) =>
        {
            var result = await service.TestConnectionAsync(req?.ServerUrl, token);
            return Results.Ok(result);
        });

        // Status & Clients & Resources
        group.MapGet("/status", async (PxeService service, CancellationToken token) =>
        {
            try
            {
                var status = await service.GetStatusAsync(token);
                return Results.Ok(status);
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 502);
            }
        });

        // Service Lifecycle (start/stop/restart)
        group.MapPost("/services/{service}/{action}", async (string service, string action, PxeService pxe, CancellationToken token) =>
        {
            try
            {
                var res = await pxe.ServiceActionAsync(service, action, token);
                return Results.Ok(res);
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 502);
            }
        });

        // Images list
        group.MapGet("/images", async (PxeService pxe, CancellationToken token) =>
        {
            try
            {
                var res = await pxe.GetImagesAsync(token);
                return Results.Ok(res);
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 502);
            }
        });

        // Delete image
        group.MapDelete("/images", async ([FromQuery] string path, PxeService pxe, CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(path)) return Results.BadRequest(new { error = "Укажите путь к файлу." });
            try
            {
                var ok = await pxe.DeleteImageAsync(path, token);
                return ok ? Results.Ok(new { success = true }) : Results.BadRequest(new { error = "Не удалось удалить файл." });
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 502);
            }
        });

        // Logs
        group.MapGet("/logs/{service}", async (string service, PxeService pxe, CancellationToken token) =>
        {
            try
            {
                var logs = await pxe.GetLogsAsync(service, token);
                return Results.Ok(logs);
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 502);
            }
        });

        // Configs (grub, syslinux, dnsmasq, preseed)
        group.MapGet("/configs/{type}", async (string type, PxeService pxe, CancellationToken token) =>
        {
            try
            {
                var cfg = await pxe.GetConfigAsync(type, token);
                return Results.Ok(cfg);
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 502);
            }
        });

        group.MapPost("/configs/{type}", async (string type, [FromBody] JsonElement body, PxeService pxe, CancellationToken token) =>
        {
            try
            {
                string content = body.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                var res = await pxe.SaveConfigAsync(type, content, token);
                return Results.Ok(res);
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 502);
            }
        });

        // Process Windows Unattended / WinPE
        group.MapPost("/process-windows", async ([FromBody] JsonElement payload, PxeService pxe, CancellationToken token) =>
        {
            try
            {
                var res = await pxe.ProcessWindowsAsync(payload, token);
                return Results.Ok(res);
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 502);
            }
        });
    }
}

public record PxeTestRequest(string? ServerUrl);
