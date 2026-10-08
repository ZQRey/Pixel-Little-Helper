using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace PixelHelper;

public sealed class NcaLayerWatchdog
{
    private readonly HubConnectionService hub;
    private readonly Action<string> showNotice;

    public NcaLayerWatchdog(HubConnectionService hub, Action<string> showNotice)
    {
        this.hub = hub;
        this.showNotice = showNotice;
    }

    public void StartDelayed(TimeSpan? delay = null)
    {
        var runDelay = delay ?? TimeSpan.FromMinutes(2.5);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(runDelay);
                await CheckAndStartNcaLayerAsync();
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
            }
        });
    }

    public async Task CheckAndStartNcaLayerAsync()
    {
        try
        {
            var existing = Process.GetProcessesByName("ncalayer");
            if (existing.Length > 0)
            {
                return; // NCALayer уже запущен
            }

            string? exePath = FindNcaLayerExecutable();
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                await ReportNcaLayerFailureAsync("Исполняемый файл ncalayer.exe не найден в системе по стандартным путям.");
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
                };
                var proc = Process.Start(psi);
                await Task.Delay(TimeSpan.FromSeconds(5));

                if (Process.GetProcessesByName("ncalayer").Length == 0)
                {
                    await ReportNcaLayerFailureAsync($"Процесс {exePath} был запущен, но не обнаружен в списке активных процессов.");
                }
                else
                {
                    showNotice(Loc.T("NcaLayerSuccess"));
                }
            }
            catch (Exception ex)
            {
                await ReportNcaLayerFailureAsync($"Исключение при вызове Process.Start: {ex.Message}\n{ex.StackTrace}");
            }
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    private static string? FindNcaLayerExecutable()
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (runKey?.GetValue("NCALayer") is string runCmd)
            {
                string clean = runCmd.Trim().Trim('"');
                if (clean.Contains(' ')) clean = clean.Split(' ')[0].Trim('"');
                if (File.Exists(clean)) return clean;
            }
        }
        catch { }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        string[] candidates =
        [
            Path.Combine(appData, "NCALayer", "ncalayer.exe"),
            Path.Combine(localAppData, "Programs", "NCALayer", "ncalayer.exe"),
            Path.Combine(programFiles, "NCALayer", "ncalayer.exe"),
            Path.Combine(programFilesX86, "NCALayer", "ncalayer.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "NCALayer", "ncalayer.exe")
        ];

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        return null;
    }

    private async Task ReportNcaLayerFailureAsync(string errorDetails)
    {
        string machine = Environment.MachineName;
        string user = Environment.UserName;
        string domain = Environment.UserDomainName;
        string os = Environment.OSVersion.VersionString;

        string title = $"Сбой автозапуска NCALayer ({machine})";
        string description = $"Автоматический контроль запуска NCALayer:\n" +
                             $"Компьютер: {machine}\n" +
                             $"Пользователь: {domain}\\{user}\n" +
                             $"ОС: {os}\n" +
                             $"Время: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n" +
                             $"Ошибка:\n{errorDetails}";

        try
        {
            int ticketId = await hub.CreateTicketAt(title, description, null, "");
            showNotice(Loc.T("NcaLayerFailedTicketCreated", ticketId));
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
            showNotice($"NCALayer не запущен: {errorDetails}");
        }
    }
}
