using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace PixelHelper;

public sealed record AdminPipeMessage(string Kind, string Data = "", int ExitCode = 0);

public static class AdminCommandPipe
{
    public const string DefaultPipeName = "PixelHelperAdminExec";
    internal static string? PipeNameOverride { get; set; }
    public static string PipeName => PipeNameOverride ?? DefaultPipeName;

    private static readonly JsonSerializerOptions PipeJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static async Task RunServerAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = CreateSecurePipeServer(PipeName);
                await server.WaitForConnectionAsync(token);
                var connected = server;
                server = null;
                _ = Task.Run(() => HandleClientAsync(connected, token), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                server?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                server?.Dispose();
                Settings.Log(ex);
                try { await Task.Delay(1000, token); } catch { break; }
            }
        }
    }

    private static NamedPipeServerStream CreateSecurePipeServer(string pipeName)
    {
        try
        {
            var security = new PipeSecurity();
            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var authUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            var networkSid = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);

            security.AddAccessRule(new PipeAccessRule(systemSid, PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(adminSid, PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(authUsersSid, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(networkSid, PipeAccessRights.FullControl, AccessControlType.Deny));

            return NamedPipeServerStreamAcl.Create(
                pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0,
                0,
                security);
        }
        catch
        {
            return new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
        }
    }

    private static async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        await using (pipe)
        using (var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true))
        await using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
        {
            var writeLock = new SemaphoreSlim(1, 1);
            async Task SendFrameAsync(AdminPipeMessage msg)
            {
                await writeLock.WaitAsync(token);
                try
                {
                    string json = JsonSerializer.Serialize(msg, PipeJson);
                    await writer.WriteLineAsync(json);
                }
                catch { }
                finally { writeLock.Release(); }
            }

            string? requestLine = await reader.ReadLineAsync(token);
            if (string.IsNullOrWhiteSpace(requestLine)) return;

            CommandEnvelope? task;
            try
            {
                task = JsonSerializer.Deserialize<CommandEnvelope>(requestLine, PipeJson);
            }
            catch
            {
                await SendFrameAsync(new AdminPipeMessage("result", "Некорректный формат команды IPC.", 87));
                return;
            }

            if (task == null || task.Payload.Length > 8000)
            {
                await SendFrameAsync(new AdminPipeMessage("result", "Недопустимая или слишком длинная команда.", 87));
                return;
            }

            var consoleEncoding = CommandExecutor.GetConsoleEncoding();
            var start = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = consoleEncoding,
                StandardErrorEncoding = consoleEncoding
            };

            if (!string.IsNullOrWhiteSpace(task.AdminUser))
            {
                start.Environment["REMOTE_ADMIN"] = task.AdminUser;
                start.Environment["PIXELHELPER_ELEVATED"] = "1";
            }

            switch (task.Type)
            {
                case "cmd":
                    start.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
                    start.Arguments = "/d /s /c \"" + task.Payload + "\"";
                    break;
                case "powershell":
                    start.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
                    string setupEncoding = $"[Console]::OutputEncoding = [System.Text.Encoding]::GetEncoding({consoleEncoding.CodePage}); $OutputEncoding = [Console]::OutputEncoding; ";
                    foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-ExecutionPolicy", "Bypass", "-Command", setupEncoding + task.Payload })
                        start.ArgumentList.Add(arg);
                    break;
                default:
                    await SendFrameAsync(new AdminPipeMessage("result", "Неизвестный тип привилегированной команды.", 87));
                    return;
            }

            using var process = new Process { StartInfo = start };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));

            var output = new StringBuilder();
            var outLock = new SemaphoreSlim(1, 1);

            async Task DrainAsync(StreamReader streamReader)
            {
                var buf = new char[1024];
                int read;
                while ((read = await streamReader.ReadAsync(buf.AsMemory(), timeout.Token)) != 0)
                {
                    await outLock.WaitAsync(timeout.Token);
                    try
                    {
                        string chunk = new(buf, 0, Math.Min(read, 131072 - output.Length));
                        if (chunk.Length == 0) continue;
                        output.Append(chunk);
                        await SendFrameAsync(new AdminPipeMessage("chunk", chunk));
                    }
                    finally { outLock.Release(); }
                }
            }

            try
            {
                process.Start();
                var stdout = DrainAsync(process.StandardOutput);
                var stderr = DrainAsync(process.StandardError);
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                    await Task.WhenAll(stdout, stderr);
                    await SendFrameAsync(new AdminPipeMessage("result", output.ToString(), process.ExitCode));
                }
                catch (OperationCanceledException)
                {
                    try { if (!process.HasExited) process.Kill(true); } catch { }
                    string interrupted = output + "\nПрервано: таймаут 3 минуты.";
                    await SendFrameAsync(new AdminPipeMessage("result", interrupted[..Math.Min(interrupted.Length, 131072)], 1460));
                }
            }
            catch (Exception ex)
            {
                await SendFrameAsync(new AdminPipeMessage("result", "Ошибка запуска в привилегированной службе: " + ex.Message, 5));
            }
        }
    }

    public static async Task<(bool Handled, string Output, int ExitCode)> TryExecuteAsync(
        CommandEnvelope task,
        Func<string, Task> emit,
        CancellationToken token)
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(1500, token);

            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

            string payloadJson = JsonSerializer.Serialize(task, PipeJson);
            await writer.WriteLineAsync(payloadJson);
            await writer.FlushAsync();

            var fullOutput = new StringBuilder();
            var jsonBuffer = new StringBuilder();
            while (!token.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(token);
                if (line == null) break;

                jsonBuffer.AppendLine(line);
                string candidate = jsonBuffer.ToString().Trim();
                if (!candidate.EndsWith('}')) continue;

                AdminPipeMessage? msg;
                try
                {
                    msg = JsonSerializer.Deserialize<AdminPipeMessage>(candidate, PipeJson);
                }
                catch
                {
                    continue;
                }
                jsonBuffer.Clear();
                if (msg == null) continue;

                if (msg.Kind == "chunk" && !string.IsNullOrEmpty(msg.Data))
                {
                    fullOutput.Append(msg.Data);
                    await emit(msg.Data);
                }
                else if (msg.Kind == "result")
                {
                    string finalText = !string.IsNullOrEmpty(msg.Data) ? msg.Data : fullOutput.ToString();
                    return (true, finalText, msg.ExitCode);
                }
            }

            return (true, fullOutput.ToString(), 0);
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
            return (false, "", -1);
        }
    }
}
