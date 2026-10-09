using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace PixelHelper;

public sealed class SessionWatchdog
{
    private readonly DispatcherTimer timer = new();
    private readonly NcaLayerWatchdog? ncaWatchdog;
    private readonly Action<string>? showNotice;
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(50);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WTS_SESSION_INFO
    {
        public int SessionId;
        public nint pWinStationName;
        public int State;
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSEnumerateSessions(nint hServer, int reserved, int version, out nint ppSessionInfo, out int pCount);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(nint pMemory);

    [DllImport("wtsapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(nint hServer, int sessionId, int wtsInfoClass, out nint ppBuffer, out int pBytesReturned);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSLogoffSession(nint hServer, int sessionId, bool bWait);

    public SessionWatchdog(NcaLayerWatchdog? ncaWatchdog = null, Action<string>? showNotice = null)
    {
        this.ncaWatchdog = ncaWatchdog;
        this.showNotice = showNotice;
    }

    public void Start(TimeSpan? initialDelay = null)
    {
        // Первичная проверка активных пользователей через 2-3 минуты после запуска
        var startupDelay = initialDelay ?? TimeSpan.FromMinutes(2.5);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(startupDelay);
                await CheckAndCleanInactiveSessionsAsync();
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
            }
        });

        // Регулярный контроль длительного простоя неактивной сессии
        timer.Interval = TimeSpan.FromMinutes(2);
        timer.Tick += (_, _) => CheckSessionActivity();
        timer.Start();
    }

    public void Stop() => timer.Stop();

    public async Task CheckAndCleanInactiveSessionsAsync()
    {
        try
        {
            int currentSessionId = Process.GetCurrentProcess().SessionId;
            string currentUserName = Environment.UserName;

            var sessions = GetUserSessions();
            if (sessions.Count <= 1)
            {
                return; // Только один пользователь в системе, очистка не требуется
            }

            // Находим сессии других пользователей (не текущая сессия)
            var otherSessions = sessions
                .Where(s => s.SessionId != currentSessionId &&
                            !string.Equals(s.UserName, currentUserName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (otherSessions.Count == 0)
            {
                return;
            }

            bool disconnectedAny = false;
            foreach (var other in otherSessions)
            {
                try
                {
                    Settings.Log(new InvalidOperationException(
                        $"Обнаружена неактивная/фоновая сессия пользователя {other.UserName} (SessionId: {other.SessionId}, State: {other.State}). Завершение для экономии ресурсов."));

                    // Завершаем сессию через Win32 API и logoff.exe
                    WTSLogoffSession(nint.Zero, other.SessionId, false);

                    try
                    {
                        var psi = new ProcessStartInfo("logoff.exe", other.SessionId.ToString())
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using var proc = Process.Start(psi);
                        proc?.WaitForExit(3000);
                    }
                    catch { }

                    disconnectedAny = true;
                    showNotice?.Invoke(Loc.T("SessionDisconnectedUserLoggedOff", other.UserName));
                }
                catch (Exception ex)
                {
                    Settings.Log(ex);
                }
            }

            // После завершения сессии неактивного пользователя проверяем и перезапускаем NCALayer
            if (disconnectedAny || otherSessions.Count > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                if (ncaWatchdog != null)
                {
                    await ncaWatchdog.RestartNcaLayerAsync();
                }
                else
                {
                    await RestartOrEnsureNcaLayerAsync();
                }
            }
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    private static async Task RestartOrEnsureNcaLayerAsync()
    {
        try
        {
            var procs = Process.GetProcessesByName("ncalayer");
            foreach (var proc in procs)
            {
                try { proc.Kill(); proc.WaitForExit(3000); } catch { }
            }

            await Task.Delay(TimeSpan.FromSeconds(2));

            string? exePath = NcaLayerWatchdog.FindNcaLayerExecutable();
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    private static List<(int SessionId, string UserName, int State)> GetUserSessions()
    {
        var result = new List<(int SessionId, string UserName, int State)>();

        // 1. Через WTSEnumerateSessions
        try
        {
            if (WTSEnumerateSessions(nint.Zero, 0, 1, out nint ppSessionInfo, out int count) && ppSessionInfo != nint.Zero)
            {
                try
                {
                    int size = Marshal.SizeOf<WTS_SESSION_INFO>();
                    for (int i = 0; i < count; i++)
                    {
                        var info = Marshal.PtrToStructure<WTS_SESSION_INFO>(ppSessionInfo + i * size);
                        if (info.SessionId == 0) continue; // Сессия 0 - системные службы

                        string? userName = GetSessionUserName(info.SessionId);
                        if (!string.IsNullOrWhiteSpace(userName) &&
                            !userName.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) &&
                            !userName.Equals("LOCAL SERVICE", StringComparison.OrdinalIgnoreCase) &&
                            !userName.Equals("NETWORK SERVICE", StringComparison.OrdinalIgnoreCase) &&
                            !userName.StartsWith("DWM-", StringComparison.OrdinalIgnoreCase) &&
                            !userName.StartsWith("UMFD-", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Add((info.SessionId, userName, info.State));
                        }
                    }
                }
                finally
                {
                    WTSFreeMemory(ppSessionInfo);
                }
            }
        }
        catch { }

        // 2. Если WTSEnumerateSessions ничего не вернул, используем quser.exe
        if (result.Count == 0)
        {
            try
            {
                var quserSessions = ParseQuserOutput();
                result.AddRange(quserSessions);
            }
            catch { }
        }

        return result;
    }

    private static string? GetSessionUserName(int sessionId)
    {
        try
        {
            if (WTSQuerySessionInformation(nint.Zero, sessionId, 5 /* WTSUserName */, out nint pBuffer, out _) && pBuffer != nint.Zero)
            {
                try
                {
                    return Marshal.PtrToStringAuto(pBuffer)?.Trim();
                }
                finally
                {
                    WTSFreeMemory(pBuffer);
                }
            }
        }
        catch { }
        return null;
    }

    private static List<(int SessionId, string UserName, int State)> ParseQuserOutput()
    {
        var list = new List<(int SessionId, string UserName, int State)>();
        try
        {
            var psi = new ProcessStartInfo("quser.exe")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return list;

            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart('>', ' ').Trim();
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    string username = parts[0].TrimStart('>');
                    // Ищем числовой идентификатор сессии
                    for (int j = 1; j < parts.Length; j++)
                    {
                        if (int.TryParse(parts[j], out int sId) && sId > 0)
                        {
                            int state = (line.Contains("Disc", StringComparison.OrdinalIgnoreCase) ||
                                         line.Contains("Отключ", StringComparison.OrdinalIgnoreCase)) ? 4 : 0;
                            list.Add((sId, username, state));
                            break;
                        }
                    }
                }
            }
        }
        catch { }
        return list;
    }

    private void CheckSessionActivity()
    {
        try
        {
            uint consoleSessionId = WTSGetActiveConsoleSessionId();
            int currentSessionId = Process.GetCurrentProcess().SessionId;

            // Если наша сессия является активной на консоли, пользователь за компьютером прямо сейчас
            if (currentSessionId == (int)consoleSessionId)
            {
                return;
            }

            // Наша сессия НЕ на консоли (произошло переключение пользователя / Fast User Switching / RDP)
            var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (GetLastInputInfo(ref lii))
            {
                uint idleTicks = (uint)Environment.TickCount - lii.dwTime;
                var idleTime = TimeSpan.FromMilliseconds(idleTicks);

                if (idleTime >= IdleThreshold)
                {
                    Settings.Log(new InvalidOperationException(
                        $"Сессия {currentSessionId} неактивна на консоли (активна {consoleSessionId}) и простаивает {idleTime.TotalMinutes:F1} мин. " +
                        $"Завершение работы клиента для освобождения ресурсов памяти и процессора."));

                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        Application.Current.Shutdown();
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }
}
