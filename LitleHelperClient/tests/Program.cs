using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PixelHelper;

static void Check(bool result, string message) { if (!result) throw new Exception(message); Console.WriteLine("PASS " + message); }

string temp = Path.Combine(Path.GetTempPath(), "PixelHelper.Tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var installedSettings = new Settings { ServerUrl = "http://old-server:5000", ClientToken = "test-key", X = 42, HubUrl = "https://explicit.example/helperHub" };
    Settings.ApplyInstalledServer(installedSettings, "https://new-server.example:5443/team/");
    Check(installedSettings.ServerUrl == "https://new-server.example:5443/team" && installedSettings.ClientToken == "test-key" && installedSettings.X == 42 && installedSettings.HubUrl == "https://explicit.example/helperHub", "installed server replaces saved address without changing user settings or explicit hub");
    foreach (string invalid in new[] { "", "file:///C:/server", "not a URL", "https://user:pass@server", "https://server/?token=secret", "https://server/#fragment" })
    {
        Settings.ApplyInstalledServer(installedSettings, invalid);
        Check(installedSettings.ServerUrl == "https://new-server.example:5443/team", "invalid installed address ignored: " + invalid);
    }
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/";
    var requests = new List<string>();
    async Task Serve(string body, int code = 200)
    {
        using var socket = await listener.AcceptTcpClientAsync();
        using var stream = socket.GetStream();
        var bytes = new List<byte>();
        while (true)
        {
            var buffer = new byte[1];
            if (await stream.ReadAsync(buffer) == 0) break;
            bytes.Add(buffer[0]);
            if (bytes.Count >= 4 && Encoding.ASCII.GetString(bytes.TakeLast(4).ToArray()) == "\r\n\r\n") break;
        }
        string headers = Encoding.UTF8.GetString(bytes.ToArray());
        int length = 0;
        foreach (var header in headers.Split("\r\n"))
            if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(header.Split(':')[1].Trim());
        var payload = new byte[length];
        if (headers.Contains("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase))
        {
            async Task<string> ReadLine()
            {
                var line = new List<byte>();
                while (true)
                {
                    var b = new byte[1]; await stream.ReadExactlyAsync(b);
                    line.Add(b[0]);
                    if (line.Count >= 2 && line[^2] == 13 && line[^1] == 10) return Encoding.ASCII.GetString(line.ToArray()).TrimEnd('\r', '\n');
                }
            }
            var bodyBytes = new List<byte>();
            while (true)
            {
                int size = Convert.ToInt32((await ReadLine()).Split(';')[0], 16);
                if (size == 0) { await ReadLine(); break; }
                var chunk = new byte[size]; await stream.ReadExactlyAsync(chunk); bodyBytes.AddRange(chunk); await ReadLine();
            }
            payload = bodyBytes.ToArray();
        }
        else await stream.ReadExactlyAsync(payload);
        requests.Add(headers + Encoding.UTF8.GetString(payload));
        byte[] content = Encoding.UTF8.GetBytes(body);
        byte[] response = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} Test\r\nContent-Type: application/json\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(response); await stream.WriteAsync(content);
    }
    var settings = new Settings { ServerUrl = url };
    using var api = new ApiClient(settings, temp);
    Check(ApiClient.Defaults().Select(a => a.Id).SequenceEqual(new[] { "search", "dmed", "eisz", "disable-startup", "exit" }), "strict five offline buttons");
    Check(api.ReadCache().Count == 5, "offline defaults");
    var serving = Serve("[{\"id\":\"test\",\"title\":\"Documents\",\"type\":\"open_path\",\"target\":\"C:\\\\Docs\"}]");
    var actions = await api.GetActionsAsync(CancellationToken.None); await serving;
    Check(actions.Count == 1 && actions[0].Id == "test", "GET actions and deserialization");
    Check(requests[0].Contains("/api/v1/assistant/actions?user=") && requests[0].Contains("&pc="), "identity query parameters");
    Check(File.Exists(Path.Combine(temp, "actions_cache.json")), "atomic disk cache");
    serving = Serve("malformed"); actions = await api.GetActionsAsync(CancellationToken.None); await serving;
    Check(actions[0].Id == "test", "malformed server response retains cache");
    serving = Serve("{}", 503); actions = await api.GetActionsAsync(CancellationToken.None); await serving;
    Check(actions[0].Id == "test", "HTTP failure retains cache");
    serving = Serve("{}"); await api.SendTicketAsync("Проверка отправки", CancellationToken.None); await serving;
    var request = requests[^1];
    using var json = JsonDocument.Parse(request[(request.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..]);
    Check(request.StartsWith("POST /api/v1/assistant/tickets") && json.RootElement.GetProperty("text").GetString() == "Проверка отправки" && json.RootElement.GetProperty("pc").GetString() == Environment.MachineName, "ticket POST body");
    serving = Serve("{}", 500);
    bool failed = false;
    try { await api.SendTicketAsync("error", CancellationToken.None); } catch (HttpRequestException) { failed = true; }
    await serving; Check(failed, "failed ticket is not reported as sent");
    listener.Stop();
    File.WriteAllText(Path.Combine(temp, "actions_cache.json"), "broken");
    Check(api.ReadCache().Count == 5, "corrupt cache fallback");
    var disabledExecutor = new CommandExecutor(new Settings { EnableAdministrativeCommands = false });
    var denied = await disabledExecutor.ExecuteAsync(new("disabled", "cmd", "echo should-not-run"), _ => Task.CompletedTask, CancellationToken.None);
    Check(denied.ExitCode == 5, "client configuration disables remote execution");
    var executor = new CommandExecutor(new Settings());
    var streamed = new StringBuilder();
    var command = await executor.ExecuteAsync(new("echo", "cmd", "echo PIXELHELPER_TEST"), s => { streamed.Append(s); return Task.CompletedTask; }, CancellationToken.None);
    Check(command.ExitCode == 0 && command.Output.Contains("PIXELHELPER_TEST") && streamed.ToString().Contains("PIXELHELPER_TEST"), "CMD output capture and streaming");
    var powershell = await executor.ExecuteAsync(new("ps", "powershell", "Write-Output 'PIXELHELPER_PS_TEST'"), _ => Task.CompletedTask, CancellationToken.None);
    Check(powershell.ExitCode == 0 && powershell.Output.Contains("PIXELHELPER_PS_TEST"), "PowerShell output capture");
    var errorCommand = await executor.ExecuteAsync(new("error", "cmd", "echo ERROR_TEST 1>&2 & exit /b 7"), _ => Task.CompletedTask, CancellationToken.None);
    Check(errorCommand.ExitCode == 7 && errorCommand.Output.Contains("ERROR_TEST"), "stderr and non-zero exit propagated");
    var snapshot = await Task.Run(SystemInspector.Collect);
    Check(snapshot.Hardware.TotalRamBytes > 0 || snapshot.Hardware.Errors.Length > 0, "WMI inventory yields RAM or explicit error");
    Check(snapshot.Software.All(s => !string.IsNullOrWhiteSpace(s.Name)), "registry software inventory names");
    Exception? spriteError = null;
    var thread = new Thread(() => { try {
    _ = new System.Windows.Application();
    var sprites = new Sprites();
    foreach (var state in Enum.GetValues<PetState>())
    for (int index = 0; index < (state == PetState.Idle ? 4 : 2); index++)
    {
        var frame = sprites.Get(state, index);
        Check(frame.Width == 48 && frame.Height == 48 && !frame.Opaque(0, 47), $"{state} {index}: size and transparent padding");
        var runs = frame.Runs(0, 0, 1).ToArray();
        for (int y = 0; y < 48; y++) for (int x = 0; x < 48; x++)
            if (frame.Opaque(x, y) != runs.Any(r => r.Contains(new System.Windows.Point(x + .5, y + .5))))
                throw new Exception("Region alpha mismatch");
    }
    Check(true, "all ten sprite regions match alpha >= 20 pixel-for-pixel");
    var pet = new PetWindow(diagnostics: true);
    var offlineActions = (List<AssistantAction>)typeof(PetWindow).GetField("actions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(pet)!;
    Check(offlineActions.Select(a => a.Id).SequenceEqual(ApiClient.Defaults().Select(a => a.Id)), "WPF offline menu ignores cached server actions");
    pet.Show(); pet.UpdateLayout();
    Check(pet.ActualWidth == 96 && pet.ActualHeight == 96, "collapsed native window is 96x96");
    Check(pet.InputHitTest(new System.Windows.Point(48, 32)) is System.Windows.Controls.Image, "robot receives WPF input after canvas translation");
    typeof(PetWindow).GetMethod("ShowMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(pet, null);
    pet.UpdateLayout();
    Check(pet.ActualWidth == 500 && pet.ActualHeight == 400, "expanded menu layout");
    Check(pet.InputHitTest(new System.Windows.Point(250, 318)) is System.Windows.Controls.Image, "robot input remains correct with menu open");
    typeof(PetWindow).GetMethod("HideBubbles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(pet, null);
    pet.UpdateLayout();
    Check(pet.ActualWidth == 96 && pet.InputHitTest(new System.Windows.Point(48, 32)) is System.Windows.Controls.Image, "menu collapse restores robot hit testing");
    pet.Close();
    } catch (Exception ex) { spriteError = ex; } });
    thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
    if (spriteError != null) throw spriteError;
    Console.WriteLine("All checks passed.");
}
finally { Directory.Delete(temp, true); }
