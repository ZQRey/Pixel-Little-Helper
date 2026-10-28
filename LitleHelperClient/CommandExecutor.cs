using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace PixelHelper;

public sealed class CommandExecutor(Settings settings)
{
    private readonly SemaphoreSlim serial = new(1, 1);

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    public static Encoding GetConsoleEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            uint oem = GetOEMCP();
            if (oem > 0) return Encoding.GetEncoding((int)oem);
        }
        catch { }
        try
        {
            int oem = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            if (oem > 0) return Encoding.GetEncoding(oem);
        }
        catch { }
        return Encoding.UTF8;
    }

    public async Task<(string Output, int ExitCode)> ExecuteAsync(CommandEnvelope task, Func<string, Task> emit, CancellationToken token)
    {
        if (!settings.EnableAdministrativeCommands) return ("Административные команды отключены в конфигурации клиента.", 5);
        if (task.Payload.Length > 8000) return ("Слишком длинная команда.", 87);
        await serial.WaitAsync(token);
        try
        {
            if (task.Type == "processes")
            {
                string processJson = JsonSerializer.Serialize(ProcessInspector.Collect(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
                return processJson.Length <= 131072 ? (processJson, 0) : ("Список процессов превышает допустимый размер ответа", 122);
            }
            if (task.Type == "kill_pid")
            {
                ProcessTarget? target;
                try { target = JsonSerializer.Deserialize<ProcessTarget>(task.Payload, Settings.Json); }
                catch (JsonException) { return ("Неверные данные выбранного процесса", 87); }
                if (target == null || target.Pid <= 0 || string.IsNullOrWhiteSpace(target.Name) || !long.TryParse(target.StartTimeUtcTicks, out long startTicks) || startTicks <= 0)
                    return ("Неверные данные выбранного процесса", 87);
                return ProcessInspector.Stop(target);
            }
            if (task.Type == "kill")
            {
                string name = Path.GetFileNameWithoutExtension(task.Payload);
                if (name.Length == 0 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.'))) return ("Неверное имя процесса", 87);
                int count = 0;
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        if (process.Id == Environment.ProcessId) return ("Нельзя завершить сам помощник этой командой", 5);
                        process.Kill(); count++;
                    }
                }
                return ($"Завершено процессов: {count}", 0);
            }
            if (task.Elevated && task.Type is "cmd" or "powershell")
            {
                var pipeResult = await AdminCommandPipe.TryExecuteAsync(task, emit, token);
                if (pipeResult.Handled)
                {
                    return (pipeResult.Output, pipeResult.ExitCode);
                }

                bool isRunningAsAdmin = false;
                try
                {
                    using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                    var principal = new System.Security.Principal.WindowsPrincipal(identity);
                    isRunningAsAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
                catch { }

                if (!isRunningAsAdmin)
                {
                    string serviceMissingMsg = "Ошибка выполнения с правами Администратора: служба повышенных привилегий PixelHelperUpdater недоступна (не запущена или не установлена), а помощник запущен с обычными правами пользователя.";
                    await emit(serviceMissingMsg);
                    return (serviceMissingMsg, 5);
                }
            }
            var consoleEncoding = GetConsoleEncoding();
            var start = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = consoleEncoding,
                StandardErrorEncoding = consoleEncoding
            };
            if (task.Elevated && !string.IsNullOrWhiteSpace(task.AdminUser))
            {
                start.Environment["REMOTE_ADMIN"] = task.AdminUser;
                start.Environment["PIXELHELPER_ELEVATED"] = "1";
            }
            switch (task.Type)
            {
                case "cmd":
                    start.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
                    start.Arguments = "/d /s /c \"" + task.Payload + "\""; break;
                case "powershell":
                    start.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
                    string setupEncoding = $"[Console]::OutputEncoding = [System.Text.Encoding]::GetEncoding({consoleEncoding.CodePage}); $OutputEncoding = [Console]::OutputEncoding; ";
                    foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", setupEncoding + task.Payload }) start.ArgumentList.Add(arg);
                    break;
                case "reboot": case "shutdown":
                    start.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");
                    foreach (string arg in new[] { task.Type == "reboot" ? "/r" : "/s", "/t", "5", "/f" }) start.ArgumentList.Add(arg);
                    break;
                default: return ("Неизвестный тип команды", 87);
            }
            using var processHandle = new Process { StartInfo = start };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            var output = new StringBuilder();
            var outputLock = new SemaphoreSlim(1, 1);
            async Task Drain(StreamReader reader)
            {
                var buffer = new char[1024];
                int read;
                while ((read = await reader.ReadAsync(buffer.AsMemory(), timeout.Token)) != 0)
                {
                    await outputLock.WaitAsync(timeout.Token);
                    try
                    {
                        string chunk = new(buffer, 0, Math.Min(read, 131072 - output.Length));
                        if (chunk.Length == 0) continue;
                        output.Append(chunk); await emit(chunk);
                    }
                    finally { outputLock.Release(); }
                }
            }
            processHandle.Start();
            var stdout = Drain(processHandle.StandardOutput); var stderr = Drain(processHandle.StandardError);
            try
            {
                await processHandle.WaitForExitAsync(timeout.Token);
                await Task.WhenAll(stdout, stderr);
                return (output.ToString(), processHandle.ExitCode);
            }
            catch (OperationCanceledException)
            {
                try { if (!processHandle.HasExited) processHandle.Kill(true); } catch (InvalidOperationException) { }
                try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
                string interrupted = output + "\nПрервано: таймаут 3 минуты или завершение помощника.";
                return (interrupted[..Math.Min(interrupted.Length, 131072)], 1460);
            }
            finally { outputLock.Dispose(); }
        }
        catch (System.ComponentModel.Win32Exception ex) { return ($"Ошибка доступа/запуска ({ex.NativeErrorCode}): {ex.Message}. Помощник работает с правами текущего пользователя; SYSTEM-служба не установлена.", ex.NativeErrorCode); }
        catch (ArgumentException) { return ("Процесс уже завершён или параметры неверны. Обновите список процессов.", 87); }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException) { return (ex.Message, 5); }
        finally { serial.Release(); }
    }
}
