using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace LitleHelperServer;

public sealed record MailBackupUploadRequest(
    string MachineName,
    string AccountEmail,
    string PayloadBase64,
    int MessageCount);

public static class MailBackupApi
{
    private static string GetBackupDir()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "Data", "MailBackups");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string SafeFileName(string machine, string email)
    {
        string safeM = string.Concat(machine.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));
        if (string.IsNullOrEmpty(safeM)) safeM = "HOST";
        string emailNormalized = email.Replace("@", "_at_");
        string safeE = string.Concat(emailNormalized.Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'));
        if (string.IsNullOrEmpty(safeE)) safeE = "MAIL";
        return $"{safeM}_{safeE}";
    }

    public static void MapMailBackupApi(this WebApplication app)
    {
        app.MapPost("/api/mail-backup/upload", async (MailBackupUploadRequest request, HttpContext context, CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(request.AccountEmail) || string.IsNullOrWhiteSpace(request.PayloadBase64))
                return Results.BadRequest(new { error = "Укажите почтовый ящик и данные архива." });

            string machine = context.Request.Headers["X-Machine-Name"].ToString();
            if (string.IsNullOrWhiteSpace(machine)) machine = request.MachineName;
            if (string.IsNullOrWhiteSpace(machine)) machine = "DEFAULT";

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(request.PayloadBase64);
            }
            catch
            {
                return Results.BadRequest(new { error = "Некорректный формат Base64." });
            }

            if (bytes.Length > 200 * 1024 * 1024)
                return Results.BadRequest(new { error = "Размер архива превышает лимит (200 МБ)." });

            string baseName = SafeFileName(machine, request.AccountEmail);
            string backupDir = GetBackupDir();
            string bakPath = Path.Combine(backupDir, baseName + ".bak");
            string metaPath = Path.Combine(backupDir, baseName + ".json");

            await File.WriteAllBytesAsync(bakPath, bytes, token);

            var meta = new
            {
                machineName = machine,
                accountEmail = request.AccountEmail,
                messageCount = request.MessageCount,
                sizeBytes = bytes.Length,
                uploadedAtUtc = DateTime.UtcNow
            };
            await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }), token);

            return Results.Ok(new { success = true, sizeBytes = bytes.Length, messageCount = request.MessageCount, uploadedAt = meta.uploadedAtUtc });
        });

        app.MapGet("/api/mail-backup/download", async (string? machineName, string accountEmail, HttpContext context, CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(accountEmail))
                return Results.BadRequest(new { error = "Укажите адрес почтового ящика." });

            string machine = context.Request.Headers["X-Machine-Name"].ToString();
            if (string.IsNullOrWhiteSpace(machine)) machine = machineName ?? "";
            if (string.IsNullOrWhiteSpace(machine)) machine = "DEFAULT";

            string baseName = SafeFileName(machine, accountEmail);
            string backupDir = GetBackupDir();
            string bakPath = Path.Combine(backupDir, baseName + ".bak");

            if (!File.Exists(bakPath))
            {
                string safeSuffix = SafeFileName("", accountEmail).TrimStart('_');
                var matching = Directory.GetFiles(backupDir, $"*_{safeSuffix}.bak");
                if (matching.Length > 0)
                {
                    bakPath = matching.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First();
                }
                else
                {
                    return Results.NotFound(new { error = "Резервная копия почты не найдена на сервере." });
                }
            }

            byte[] bytes = await File.ReadAllBytesAsync(bakPath, token);
            return Results.File(bytes, "application/octet-stream", $"{accountEmail}_backup.bak");
        });

        app.MapGet("/api/mail-backup/status", async (string? machineName, string accountEmail, HttpContext context, CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(accountEmail))
                return Results.BadRequest(new { error = "Укажите адрес почтового ящика." });

            string machine = context.Request.Headers["X-Machine-Name"].ToString();
            if (string.IsNullOrWhiteSpace(machine)) machine = machineName ?? "";
            if (string.IsNullOrWhiteSpace(machine)) machine = "DEFAULT";

            string baseName = SafeFileName(machine, accountEmail);
            string backupDir = GetBackupDir();
            string bakPath = Path.Combine(backupDir, baseName + ".bak");
            string metaPath = Path.Combine(backupDir, baseName + ".json");

            if (!File.Exists(bakPath))
            {
                string safeSuffix = SafeFileName("", accountEmail).TrimStart('_');
                var matching = Directory.GetFiles(backupDir, $"*_{safeSuffix}.bak");
                if (matching.Length > 0)
                {
                    bakPath = matching.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First();
                    metaPath = Path.ChangeExtension(bakPath, ".json");
                }
            }

            if (!File.Exists(bakPath))
            {
                return Results.Ok(new { exists = false });
            }

            long size = new FileInfo(bakPath).Length;
            DateTime time = File.GetLastWriteTimeUtc(bakPath);
            int count = 0;
            if (File.Exists(metaPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(metaPath, token));
                    if (doc.RootElement.TryGetProperty("messageCount", out var p) || doc.RootElement.TryGetProperty("MessageCount", out p)) count = p.GetInt32();
                }
                catch { }
            }

            return Results.Ok(new { exists = true, sizeBytes = size, messageCount = count, updatedAtUtc = time });
        });
    }
}
