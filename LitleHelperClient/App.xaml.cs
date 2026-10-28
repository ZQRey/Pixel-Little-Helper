using System.Windows;
using System.Threading;

namespace PixelHelper;

public partial class App : Application
{
    private Mutex? singleton;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length > 0 && (e.Args[0].StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
                                  e.Args[0].StartsWith("edge-extension://", StringComparison.OrdinalIgnoreCase) ||
                                  e.Args[0] == "--native-host"))
        {
            NativeMessagingHost.Run();
            Shutdown();
            return;
        }
        if (e.Args.Contains("--update-service"))
        {
            System.ServiceProcess.ServiceBase.Run(new UpdateService()); Shutdown(); return;
        }
        if (e.Args.Contains("--cleanup-machine"))
        {
            Shutdown(MachineCleanup.Run());
            return;
        }
        if (e.Args.Contains("--reset-user-data"))
        {
            Settings.ResetUserData();
            Shutdown();
            return;
        }
        var appIcon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/PixelHelper;component/Assets/PixelHelper.ico")); appIcon.Freeze();
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender,args) => { if (sender is Window window && ReferenceEquals(args.OriginalSource,window) && window.Icon==null) window.Icon=appIcon; }));
        string? guardUrl = null;
        string? scanTarget = null;
        string? mailTarget = null;
        for (int i = 0; i < e.Args.Length; i++)
        {
            if ((e.Args[i] == "--scan" || e.Args[i] == "-scan") && i + 1 < e.Args.Length)
            {
                scanTarget = e.Args[i + 1];
                continue;
            }
            if ((e.Args[i] == "--open-mail" || e.Args[i] == "-mail") && i + 1 < e.Args.Length)
            {
                mailTarget = e.Args[i + 1];
                continue;
            }
            if (e.Args[i].EndsWith(".eml", StringComparison.OrdinalIgnoreCase) || e.Args[i].EndsWith(".mbox", StringComparison.OrdinalIgnoreCase))
            {
                if (System.IO.File.Exists(e.Args[i]))
                {
                    mailTarget = e.Args[i];
                    continue;
                }
                continue;
            }
            if (e.Args[i] == "--guard-url" && i + 1 < e.Args.Length)
            {
                guardUrl = e.Args[i + 1];
                continue;
            }
            if (e.Args[i].StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                e.Args[i].StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                guardUrl = e.Args[i];
                continue;
            }
        }
        singleton = new Mutex(true, @"Local\PixelHelper-" + Environment.UserName, out bool created);
        if (!created)
        {
            if (!string.IsNullOrWhiteSpace(scanTarget))
            {
                AntivirusScanIpc.SendScanPathToRunningInstance(scanTarget);
            }
            if (!string.IsNullOrWhiteSpace(guardUrl))
            {
                WebGuardIpc.SendUrlToRunningInstance(guardUrl);
            }
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            Settings.Log(args.Exception);
            MessageBox.Show("Не удалось продолжить работу. Подробности: %APPDATA%\\PixelHelper\\error.log", "PixelHelper");
            args.Handled = true;
            Shutdown(1);
        };
        bool diagnostics = e.Args.Length == 2 && e.Args[0] == "--diagnostics";
        MainWindow = new PetWindow(diagnostics, initialScanPath: scanTarget);
        if (!diagnostics)
        {
            NativeMethods.RegisterApplicationRestart(null, 0);
            WebGuardIpc.StartServer(url => Dispatcher.InvokeAsync(() => ((PetWindow)MainWindow).HandleGuardedUrl(url)));
            AntivirusScanIpc.StartServer(path => Dispatcher.InvokeAsync(() => ((PetWindow)MainWindow).HandlePathScanRequest(path)));
            if (!string.IsNullOrWhiteSpace(guardUrl))
            {
                Dispatcher.InvokeAsync(() => ((PetWindow)MainWindow).HandleGuardedUrl(guardUrl));
            }
            if (!string.IsNullOrWhiteSpace(mailTarget))
            {
                Dispatcher.InvokeAsync(() => ((PetWindow)MainWindow).OpenMailFile(mailTarget));
            }
        }
        MainWindow.Show();
        // Older updater watchers launch the GUI with SW_HIDE. Restore it without stealing focus.
        NativeMethods.ShowWindow(new System.Windows.Interop.WindowInteropHelper(MainWindow).Handle, 4);
        ((PetWindow)MainWindow).RestoreDisplayPreferences();
        if (!diagnostics) UpdateRestart.Watch(MainWindow);
        if (diagnostics)
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            var started = DateTime.UtcNow;
            var cpuStart = process.TotalProcessorTime;
            bool warmingUp = true;
            timer.Interval = TimeSpan.FromSeconds(5);
            timer.Tick += (_, _) =>
            {
                if (warmingUp)
                {
                    warmingUp = false; started = DateTime.UtcNow;
                    cpuStart = process.TotalProcessorTime;
                    timer.Interval = TimeSpan.FromSeconds(30);
                    return;
                }
                timer.Stop(); process.Refresh();
                double cpu = (process.TotalProcessorTime - cpuStart).TotalSeconds / (DateTime.UtcNow - started).TotalSeconds / Environment.ProcessorCount * 100;
                var hwnd = new System.Windows.Interop.WindowInteropHelper(MainWindow).Handle;
                int samples = 0, visible = 0;
                for (int y = 300; y < 370; y += 4) for (int x = 215; x < 278; x += 4)
                {
                    var p = ((PetWindow)MainWindow).DiagnosticPoint(x, y);
                    var hit = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = (int)p.X, Y = (int)p.Y });
                    samples++; if (hit == hwnd) visible++;
                }
                System.IO.File.WriteAllText(e.Args[1], System.Text.Json.JsonSerializer.Serialize(new
                {
                    workingSetMiB = process.WorkingSet64 / 1048576.0,
                    privateMiB = process.PrivateMemorySize64 / 1048576.0,
                    cpuPercent = cpu,
                    desktopHitSamples = visible,
                    sampleCount = samples,
                    animatedFrames = ((PetWindow)MainWindow).AnimatedFrames,
                    bottomRepositions = NativeMethods.BottomChanges,
                    note = "5-second warmup, then 30-second measurement. Animation pauses when covered. Hit count depends on foreground windows. No registry/config changes."
                }, Settings.Json));
                MainWindow.Close();
            };
            timer.Start();
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        AntivirusScanIpc.StopServer();
        WebGuardIpc.StopServer();
        singleton?.Dispose();
        base.OnExit(e);
    }
}
