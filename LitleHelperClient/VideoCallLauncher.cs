using System.Diagnostics;
using System.IO;

namespace PixelHelper;

/// <summary>
/// Запускает нативное окно звонка через VideoCallWindow (WPF + WebView2),
/// либо автономное окно браузера без адресной строки в качестве резервного варианта.
/// </summary>
public static class VideoCallLauncher
{
    public static void LaunchCall(string serverAddress, int peerId, string targetName, bool isVideo, bool isCaller, int currentUserId, string? callId = null, string? token = null)
    {
        try
        {
            // Запуск нативного WPF-окна звонка в UI-потоке
            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    VideoCallWindow.OpenCall(serverAddress, peerId, targetName, isVideo, isCaller, currentUserId, callId, token);
                });
                return;
            }

            VideoCallWindow.OpenCall(serverAddress, peerId, targetName, isVideo, isCaller, currentUserId, callId, token);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VideoCallLauncher] Ошибка открытия VideoCallWindow: {ex.Message}. Запуск резервного окна.");
            LaunchFallback(serverAddress, peerId, targetName, isVideo, isCaller, currentUserId, callId, token);
        }
    }

    public static void LaunchFallback(string serverAddress, int peerId, string targetName, bool isVideo, bool isCaller, int currentUserId, string? callId = null, string? token = null)
    {
        CallRecordingReceiver.Start();

        string host = serverAddress.TrimEnd('/');
        string url = $"{host}/messenger/call.html?peer={peerId}&video={(isVideo ? 1 : 0)}&caller={(isCaller ? 1 : 0)}&user={currentUserId}&name={Uri.EscapeDataString(targetName)}&port={CallRecordingReceiver.LocalPort}&v=1.2.51";
        if (!string.IsNullOrEmpty(callId))
        {
            url += $"&callId={Uri.EscapeDataString(callId)}";
        }
        if (!string.IsNullOrEmpty(token))
        {
            url += $"&token={Uri.EscapeDataString(token)}";
        }

        string? browserPath = FindAppBrowserPath();

        if (browserPath != null && File.Exists(browserPath))
        {
            string profileDir = Path.Combine(Path.GetTempPath(), "PixelHelper", "CallSession");
            Directory.CreateDirectory(profileDir);

            var psi = new ProcessStartInfo
            {
                FileName = browserPath,
                Arguments = $"--app=\"{url}\" --window-size=1080,720 --window-position=center --user-data-dir=\"{profileDir}\" --enable-media-stream",
                UseShellExecute = false
            };

            try
            {
                Process.Start(psi);
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VideoCallLauncher] Не удалось запустить браузер в режиме --app: {ex.Message}");
            }
        }

        // Запуск по умолчанию через системную оболочку
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    private static string? FindAppBrowserPath()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "Application", "msedge.exe")
        ];

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        return null;
    }
}
