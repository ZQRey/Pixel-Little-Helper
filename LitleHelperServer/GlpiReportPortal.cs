using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace LitleHelperServer;

// GLPI is the authenticating party. This endpoint never issues a panel token.
// Only read-only report operations are accepted, scoped to the signed active entities.
public static class GlpiReportPortal
{
    private static readonly ConcurrentDictionary<string, long> Nonces = new();
    public record Request(string Operation, string Query, int UserId, string Username, int[] Entities);
    public static void MapGlpiReportPortal(this WebApplication app)
    {
        app.MapPost("/api/glpi-report-portal", async (HttpContext http, IConfiguration config, GlpiReports reports, TicketSurveys surveys, HelperDb db, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var keyFile = config["GlpiReports:BridgeKeyFile"] ?? "data/glpi-rating-bridge.key";
            if (!File.Exists(keyFile) || http.Request.ContentLength > 16000) return Results.Unauthorized();
            using var reader = new StreamReader(http.Request.Body, Encoding.UTF8);
            var buffer = new char[16001]; int length = 0, read;
            while (length < buffer.Length && (read = await reader.ReadAsync(buffer.AsMemory(length), ct)) > 0) length += read;
            if (length > 16000) return Results.BadRequest();
            string raw = new(buffer, 0, length), stamp = http.Request.Headers["X-Helper-Time"].ToString(), nonce = http.Request.Headers["X-Helper-Nonce"].ToString();
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!long.TryParse(stamp, out long time) || time < now - 120 || time > now + 120 || nonce.Length != 32 || !nonce.All(char.IsAsciiHexDigit)) return Results.Unauthorized();
            byte[] expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes((await File.ReadAllTextAsync(keyFile, ct)).Trim()), Encoding.UTF8.GetBytes("GLPI-REPORTS\n" + stamp + "\n" + nonce + "\n" + raw));
            byte[] supplied;
            try { supplied = Convert.FromHexString(http.Request.Headers["X-Helper-Signature"].ToString()); } catch (FormatException) { return Results.Unauthorized(); }
            if (!CryptographicOperations.FixedTimeEquals(expected, supplied)) return Results.Unauthorized();
            foreach (var pair in Nonces) if (pair.Value < now - 300) Nonces.TryRemove(pair.Key, out _);
            if (!Nonces.TryAdd(nonce, now)) return Results.Conflict();
            var request = JsonSerializer.Deserialize<Request>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (request == null || request.UserId < 1 || string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 255 || request.Entities == null || request.Entities.Length > 1000 || request.Entities.Any(x => x < 0) || request.Query == null || request.Query.Length > 6000) return Results.BadRequest();
            if (request.Operation.StartsWith("survey-",StringComparison.Ordinal)) {
                var person = new PanelUser { Username = request.Username, PasswordHash = "!AD", IsActive = true };
                if (request.Operation == "survey-gate") {
                    try { await surveys.EnsureCanCreate(person,false,ct,request.UserId); return Results.Json(new { required = false }); }
                    catch (Microsoft.AspNetCore.SignalR.HubException) { return Results.Json(new { required = true }); }
                }
                if (request.Operation == "survey-urgent") { await surveys.EnsureCanCreate(person,true,ct,request.UserId);return Results.Json(new { ok=true }); }
                if (request.Operation == "survey-list") {
                    var ownIds = await db.TicketSurveyInvitations.AsNoTracking().Where(i=>i.Source==reports.Source && i.RequesterId==request.UserId).Select(i=>i.Id).ToListAsync(ct);
                    return Results.Json((await surveys.Pending(person,ct)).Where(i=>ownIds.Contains(i.Id)));
                }
                using var input = JsonDocument.Parse(request.Query);
                string id = input.RootElement.GetProperty("id").GetString() ?? "";
                if (!await db.TicketSurveyInvitations.AnyAsync(i=>i.Id==id && i.Source==reports.Source && i.RequesterId==request.UserId,ct)) return Results.StatusCode(403);
                if (request.Operation == "survey-claim") return Results.Json(new { claimed=await surveys.Claim(person,id,"glpi:"+request.UserId,ct) });
                if (request.Operation == "survey-answer") return Results.Json(await surveys.Answer(person,id,input.RootElement.Deserialize<TicketSurveyAnswer>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!,ct));
                if (request.Operation == "survey-snooze") { await surveys.Defer(person,id,false,ct,input.RootElement.GetProperty("minutes").GetInt32());return Results.Json(new { ok=true }); }
                return Results.BadRequest();
            }
            if (request.Operation is not ("report" or "xlsx" or "pdf")) return Results.BadRequest();
            // Synthetic identity exists only in this call; no database account or elevated session is created.
            var user = new PanelUser { Username = "GLPI:" + request.UserId + ":" + request.Username, Permissions = new() { ["reports.glpi.all"] = true } };
            var filter = ReportFilter.Parse(new QueryCollection(QueryHelpers.ParseQuery(request.Query)));
            var data = JsonSerializer.SerializeToElement(await reports.Report(user, filter, ct, request.Entities), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (request.Operation == "report") return Results.Json(data);
            var rows = data.GetProperty("tickets").Deserialize<ReportTicket[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
            if (rows.Length > 10000) return Results.BadRequest(new { error = "Уменьшите период: больше 10000 заявок" });
            byte[] bytes = request.Operation == "xlsx" ? GlpiReportExport.Xlsx(data, rows, filter) : GlpiReportExport.Pdf(data, rows, filter);
            db.AuditLogs.Add(new() { AdminUsername = user.Username, CommandType = "glpi_report_export", CommandPayload = request.Operation + " " + filter.From + " " + filter.To, Status = "Completed", Result = rows.Length + " заявок; GLPI session" });
            await db.SaveChangesAsync(ct);
            return Results.File(bytes, request.Operation == "xlsx" ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "application/pdf");
        }).RequireRateLimiting("glpi-portal");
    }
}
