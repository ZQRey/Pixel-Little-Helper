using System.Diagnostics;
using System.IO;
using System.Management;
using System.Windows.Threading;

namespace PixelHelper;

public sealed record PrinterStatusInfo(string Name, bool IsDefault, bool HasError, string StatusText, int JobCount);

public sealed class PrinterWatchdog : IDisposable
{
    private readonly DispatcherTimer timer = new();
    private readonly CancellationTokenSource lifetime = new();
    private int checking;
    private readonly Action<string, Action?>? showNotice;
    private DateTime lastAlert = DateTime.MinValue;
    private static readonly TimeSpan AlertCooldown = TimeSpan.FromMinutes(20);

    public PrinterWatchdog(Action<string, Action?>? showNotice = null, TimeSpan? interval = null)
    {
        this.showNotice = showNotice;
        timer.Interval = interval ?? TimeSpan.FromMinutes(3);
        timer.Tick += async (_, _) => await CheckAsync();
    }

    public async void Start(TimeSpan? initialDelay = null)
    {
        try {
            await Task.Delay(initialDelay ?? TimeSpan.FromSeconds(45), lifetime.Token);
            if (lifetime.IsCancellationRequested) return;
            await CheckAsync();
            if (!lifetime.IsCancellationRequested) timer.Start();
        } catch (OperationCanceledException) { }
    }
    private async Task CheckAsync()
    {
        if (lifetime.IsCancellationRequested || Interlocked.Exchange(ref checking,1) != 0) return;
        try { await Task.Run(CheckPrinters); }
        catch (Exception ex) { Settings.Log(ex); }
        finally { Interlocked.Exchange(ref checking,0); }
    }
    public void Stop() { lifetime.Cancel(); timer.Stop(); }
    public void Dispose() => Stop();

    public static List<PrinterStatusInfo> GetPrinters()
    {
        var list = new List<PrinterStatusInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, Default, PrinterState, PrinterStatus, ExtendedPrinterStatus, SpoolEnabled FROM Win32_Printer");
            searcher.Options.Timeout = TimeSpan.FromSeconds(5);
            foreach (ManagementObject p in searcher.Get())
            {
                string name = p["Name"]?.ToString() ?? "Принтер";
                bool isDefault = p["Default"] is true;
                uint state = p["PrinterState"] is uint s ? s : 0;
                uint status = p["PrinterStatus"] is uint st ? st : 0;

                bool hasError = false;
                string statusText = "Готов к печати";

                // Error states: 1 = Other, 2 = Unknown, 3 = Idle, 4 = Printing, 5 = Warmup, 7 = Offline, 9 = Error
                if (state == 9 || status == 7 || status == 1 || status == 2)
                {
                    hasError = true;
                    statusText = "Ошибка принтера или не в сети";
                }
                else if (state == 128 || state == 1024)
                {
                    hasError = true;
                    statusText = "Нет бумаги или замятие";
                }

                int jobs = GetJobCount(name);
                if (jobs > 0 && hasError)
                {
                    statusText = $"Зависло заданий: {jobs}";
                }

                list.Add(new(name, isDefault, hasError, statusText, jobs));
            }
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
        return list;
    }

    public static int GetJobCount(string? printerName = null)
    {
        int count = 0;
        try
        {
            string query = string.IsNullOrWhiteSpace(printerName)
                ? "SELECT JobId FROM Win32_PrintJob"
                : $"SELECT JobId FROM Win32_PrintJob WHERE Name LIKE '{printerName.Replace("'", "''")}%'";
            using var searcher = new ManagementObjectSearcher(query);
            searcher.Options.Timeout = TimeSpan.FromSeconds(5);
            foreach (ManagementObject _ in searcher.Get())
            {
                count++;
            }
        }
        catch { }
        return count;
    }

    internal static Func<(bool Success, string Message)>? QueueCleanerForTests;
    public static (bool Success, string Message) ClearPrintQueue()
    {
        if (QueueCleanerForTests != null) return QueueCleanerForTests();
        int deletedJobs = 0;
        int deletedFiles = 0;

        // 1. Попытка отменить задания печати через WMI Win32_PrintJob (работает в контексте пользователя)
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PrintJob");
            searcher.Options.Timeout = TimeSpan.FromSeconds(5);
            foreach (ManagementObject job in searcher.Get())
            {
                try
                {
                    job.Delete();
                    deletedJobs++;
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }

        // 2. Попытка очистить очереди через System.Printing (WPF PrintQueue.Purge)
        try
        {
            using var printServer = new System.Printing.LocalPrintServer();
            var queues = printServer.GetPrintQueues();
            foreach (var q in queues)
            {
                try
                {
                    q.Refresh();
                    if (q.NumberOfJobs > 0)
                    {
                        q.Purge();
                    }
                }
                catch { }
            }
        }
        catch { }

        // 3. Дополнительно: отмена заданий через PowerShell без вызова UAC
        try
        {
            RunProcess("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Get-PrintJob -ErrorAction SilentlyContinue | Remove-PrintJob -ErrorAction SilentlyContinue\"");
        }
        catch { }

        // 4. Попытка прямой очистки файлов spool (если процесс запущен с правами администратора)
        try
        {
            RunProcess("net.exe", "stop spooler");

            string spoolPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "spool", "PRINTERS");
            if (Directory.Exists(spoolPath))
            {
                try
                {
                    foreach (var file in Directory.GetFiles(spoolPath, "*.*"))
                    {
                        try { File.Delete(file); deletedFiles++; } catch { }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Обычный непривилегированный пользователь - доступ к папке spool/PRINTERS ограничен Windows.
                    // Игнорируем, так как WMI и Purge уже отменили задания на уровне подсистемы печати.
                }
            }
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
        finally
        {
            try { RunProcess("net.exe", "start spooler"); } catch { }
        }

        // 5. Проверяем оставшееся количество заданий
        int remaining = GetJobCount();
        if (remaining == 0)
        {
            int total = Math.Max(deletedJobs, deletedFiles);
            return (true, total > 0
                ? $"Очередь печати успешно очищена! Удалено заданий: {total}."
                : "Очередь печати пуста и готова к работе.");
        }

        if (deletedJobs > 0 || deletedFiles > 0)
        {
            return (true, $"Часть заданий печати отменена ({Math.Max(deletedJobs, deletedFiles)}). Оставшиеся {remaining} заблокированы системой.");
        }

        return (false, $"В очереди остаётся {remaining} заданий. Для их принудительного удаления требуются права администратора или перезагрузка ПК.");
    }

    private static void RunProcess(string fileName, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc != null) {
                var output = proc.StandardOutput.ReadToEndAsync();
                var error = proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit(5000)) proc.Kill(entireProcessTree:true);
            }
        }
        catch { }
    }

    public void CheckPrinters()
    {
        try
        {
            var printers = GetPrinters();
            var problematic = printers.FirstOrDefault(p => p.HasError && p.JobCount > 0)
                           ?? printers.FirstOrDefault(p => p.HasError && p.IsDefault);

            if (problematic != null)
            {
                if (DateTime.UtcNow - lastAlert >= AlertCooldown)
                {
                    lastAlert = DateTime.UtcNow;
                    string notice = $"В принтере \"{problematic.Name}\" возникли проблемы ({problematic.StatusText}).";
                    if (!lifetime.IsCancellationRequested)
                        timer.Dispatcher.InvokeAsync(() => { if (!lifetime.IsCancellationRequested) showNotice?.Invoke(notice, async () => {
                            var result = await Task.Run(ClearPrintQueue); showNotice?.Invoke(result.Message,null);
                        }); });
                }
            }
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }
}
