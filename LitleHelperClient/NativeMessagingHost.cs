using System.IO;
using System.Text;
using System.Text.Json;

namespace PixelHelper;

public static class NativeMessagingHost
{
    public static void Run()
    {
        try
        {
            using var stdin = Console.OpenStandardInput();
            using var stdout = Console.OpenStandardOutput();
            byte[] lenBytes = new byte[4];

            var settings = Settings.Load();

            while (stdin.Read(lenBytes, 0, 4) == 4)
            {
                int len = BitConverter.ToInt32(lenBytes, 0);
                if (len <= 0 || len > 1024 * 1024) break;

                byte[] buffer = new byte[len];
                int totalRead = 0;
                while (totalRead < len)
                {
                    int r = stdin.Read(buffer, totalRead, len - totalRead);
                    if (r <= 0) break;
                    totalRead += r;
                }

                string msg = Encoding.UTF8.GetString(buffer, 0, totalRead);
                string responseJson = ProcessMessage(msg, settings);

                byte[] respBytes = Encoding.UTF8.GetBytes(responseJson);
                byte[] respLen = BitConverter.GetBytes(respBytes.Length);
                stdout.Write(respLen, 0, 4);
                stdout.Write(respBytes, 0, respBytes.Length);
                stdout.Flush();
            }
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    public static string ProcessMessage(string json, Settings? settings = null)
    {
        try
        {
            settings ??= Settings.Load();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("url", out var urlProp))
            {
                string url = urlProp.GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(url))
                {
                    var result = WebGuard.Check(url, settings);
                    if (result.Verdict != WebGuardVerdict.Safe)
                    {
                        // Notify running desktop assistant window so character reacts with speech bubble/tray
                        WebGuardIpc.SendUrlToRunningInstance(url, launchOnFailure: false);
                        return JsonSerializer.Serialize(new
                        {
                            verdict = "Blocked",
                            reason = result.Reason,
                            host = result.Host
                        });
                    }
                    return JsonSerializer.Serialize(new { verdict = "Safe" });
                }
            }
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
        return JsonSerializer.Serialize(new
        {
            verdict = "Blocked",
            reason = "Не удалось проверить ссылку. Переход заблокирован для безопасности.",
            host = ""
        });
    }
}
