using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace PixelHelper;

public partial class AntiTheftLockWindow : Window
{
    private static AntiTheftLockWindow? currentInstance;
    private static readonly object instanceLock = new();
    private static bool isLocked = true;
    private readonly DispatcherTimer topmostTimer;

    public AntiTheftLockWindow(string? message = null)
    {
        InitializeComponent();

        if (!string.IsNullOrWhiteSpace(message))
        {
            MessageText.Text = message;
        }

        DeviceInfoText.Text = $"Устройство: {Environment.MachineName} | UUID: {SystemInspector.GetHardwareUuid()}";

        // Keep topmost and foregrounded
        topmostTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        topmostTimer.Tick += (_, _) =>
        {
            if (isLocked)
            {
                if (!Topmost) Topmost = true;
                Activate();
            }
        };
        topmostTimer.Start();

        KeyDown += AntiTheftLockWindow_KeyDown;
    }

    public static void EnsureShown(string? message = null)
    {
        lock (instanceLock)
        {
            isLocked = true;
            if (currentInstance == null)
            {
                currentInstance = new AntiTheftLockWindow(message);
                currentInstance.Show();
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    currentInstance.MessageText.Text = message;
                }
                currentInstance.WindowState = WindowState.Maximized;
                currentInstance.Topmost = true;
                currentInstance.Activate();
            }
        }
    }

    public static void CloseIfOpen()
    {
        lock (instanceLock)
        {
            isLocked = false;
            if (currentInstance != null)
            {
                try
                {
                    currentInstance.topmostTimer.Stop();
                    currentInstance.Close();
                }
                catch { }
                currentInstance = null;
            }
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (isLocked)
        {
            e.Cancel = true; // Cannot close while device is in lockdown
            return;
        }
        base.OnClosing(e);
    }

    private void AntiTheftLockWindow_KeyDown(object sender, KeyEventArgs e)
    {
        // Suppress Escape, Alt+F4, Tab, etc.
        if (e.Key is Key.Escape or Key.System or Key.Tab or Key.F4)
        {
            e.Handled = true;
        }
    }

}

