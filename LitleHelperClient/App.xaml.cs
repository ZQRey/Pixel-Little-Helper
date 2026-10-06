using System.Windows;
using System.Threading;

namespace PixelHelper;

public partial class App : Application
{
    private Mutex? singleton;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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
        singleton = new Mutex(true, @"Local\PixelHelper-" + Environment.UserName, out bool created);
        if (!created) { Shutdown(); return; }
        DispatcherUnhandledException += (_, args) =>
        {
            Settings.Log(args.Exception);
            MessageBox.Show("Не удалось продолжить работу. Подробности: %APPDATA%\\PixelHelper\\error.log", "PixelHelper");
            args.Handled = true;
            Shutdown(1);
        };
        bool diagnostics = e.Args.Length == 2 && e.Args[0] == "--diagnostics";
        MainWindow = new PetWindow(diagnostics);
        MainWindow.Show();
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
        singleton?.Dispose();
        base.OnExit(e);
    }
}
