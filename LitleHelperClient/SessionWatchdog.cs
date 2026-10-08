using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace PixelHelper;

public sealed class SessionWatchdog
{
    private readonly DispatcherTimer timer = new();
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

    public void Start()
    {
        timer.Interval = TimeSpan.FromMinutes(2);
        timer.Tick += (_, _) => CheckSessionActivity();
        timer.Start();
    }

    public void Stop() => timer.Stop();

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
