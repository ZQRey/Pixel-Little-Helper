using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PixelHelper;

Settings.TestFolder = Path.Combine(Path.GetTempPath(), "PixelHelper-tests-" + Guid.NewGuid().ToString("N"));
static void Check(bool result, string message) { if (!result) throw new Exception(message); Console.WriteLine("PASS " + message); }
Check(Settings.Folder.StartsWith(Path.GetTempPath(),StringComparison.OrdinalIgnoreCase)&&!Settings.Folder.Equals(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"PixelHelper"),StringComparison.OrdinalIgnoreCase),"tests isolate client settings from the installed helper");
Check(MachineIdentity.Scope("http://helper.gp1.loc")==MachineIdentity.Scope("https://HELPER.gp1.loc/"),"machine key survives HTTP to HTTPS migration");
Check(MachineIdentity.Scope("https://other.gp1.loc")!=MachineIdentity.Scope("https://helper.gp1.loc"),"machine key is scoped to its own server");

var robotClicks = new RobotClicks(); var clickTime = DateTime.UtcNow;
Check(robotClicks.Register(clickTime,false)==RobotClickReaction.None && robotClicks.Register(clickTime.AddMilliseconds(150),false)==RobotClickReaction.None && robotClicks.Register(clickTime.AddMilliseconds(300),false)==RobotClickReaction.Greeting,"third rapid robot click triggers greeting");
Check(robotClicks.Register(clickTime.AddMilliseconds(450),false)==RobotClickReaction.None && robotClicks.Register(clickTime.AddMilliseconds(600),false)==RobotClickReaction.Offended,"fifth rapid robot click replaces greeting with offended reaction");
Check(robotClicks.Register(clickTime.AddSeconds(1),false)==RobotClickReaction.None && robotClicks.Register(clickTime.AddSeconds(1.2),false)==RobotClickReaction.None && robotClicks.Register(clickTime.AddSeconds(1.4),false)==RobotClickReaction.None,"offended reaction cooldown prevents repeated bubbles");
robotClicks = new RobotClicks(); robotClicks.Register(clickTime,false); robotClicks.Register(clickTime.AddMilliseconds(100),true);
Check(robotClicks.Register(clickTime.AddMilliseconds(200),false)==RobotClickReaction.None && robotClicks.Register(clickTime.AddMilliseconds(300),false)==RobotClickReaction.None,"drag interrupts click sequence");
robotClicks = new RobotClicks(); robotClicks.Register(clickTime,false);
Check(robotClicks.Register(clickTime.AddSeconds(2),false)==RobotClickReaction.None && robotClicks.Register(clickTime.AddSeconds(4),false)==RobotClickReaction.None,"slow individual clicks do not trigger reaction");
if (args.Length > 0 && args[0] == "--messenger-windows")
{
    await using var messenger = new MessengerClient(new Settings { ServerUrl = args.Length > 1 ? args[1] : "https://helper.gp1.loc" });
    await messenger.StartAsync();
    Check(messenger.SignedIn, messenger.SignInStatus);
    Console.WriteLine("Windows SSO user: " + messenger.FullName + " (ID " + messenger.UserId + ")");
    await messenger.SignInWindowsAsync(true);
    Check(messenger.SignedIn, "Windows SSO renews automatically without a password");
    Check((await messenger.UsersAsync()).All(u => u.Id != messenger.UserId), "authenticated messenger directory excludes current user");
    return;
}
if (args.FirstOrDefault() == "--group-preview")
{
    await using var messenger = new MessengerClient(Settings.Load()); await messenger.StartAsync();
    Check(messenger.SignedIn, messenger.SignInStatus);
    var group = (await messenger.UsersAsync()).First(c => c.IsGroup);
    Exception? failure = null;
    var thread = new Thread(() => {
        try {
            var window = new ChatGroupWindow(messenger, group.Id, Settings.Load()); window.Show();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) => {
                timer.Stop(); window.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(window);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using(var output=File.Create(Path.Combine(Environment.CurrentDirectory,"LitleHelperClient/artifacts/group-management-preview.png"))) png.Save(output);
                window.Close(); System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }; timer.Start(); System.Windows.Threading.Dispatcher.Run();
        } catch(Exception ex) { failure=ex; }
    }); thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
    if(failure!=null) throw failure; Check(true,"real group management renders with Windows SSO"); return;
}
if (args.FirstOrDefault() == "--agent-connection")
{
    await using var agent = new HubConnectionService(Settings.Load()); agent.Start();
    var deadline = DateTime.UtcNow.AddSeconds(35);
    while (!agent.IsOnline && DateTime.UtcNow < deadline) await Task.Delay(250);
    Check(agent.IsOnline, "real helper connects: " + agent.Status);
    var first = agent.LastSuccess; await Task.Delay(TimeSpan.FromSeconds(32));
    Check(agent.IsOnline && agent.LastSuccess > first, "real helper heartbeat advances without restart"); return;
}

string temp = Path.Combine(Path.GetTempPath(), "PixelHelper.Tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var fresh = new Settings(); var other = new Settings();
    var emojiMessage = new ChatEntry(1, 9, 2, "Привет :helper_wave: :helper_joy: :helper_party: :helper_thanks: 😄 :helper_unknown:", "emoji-guid", DateTime.UtcNow, null);
    Check(HelperEmojis.Parse(emojiMessage.Body).Count() == 4, "only known helper emoji codes parsed alongside Unicode and unknown codes");
    Check(HelperEmojis.PlainText(emojiMessage.Body).Contains("[Привет]") && HelperEmojis.PlainText(emojiMessage.Body).Contains(":helper_unknown:"), "notification emoji labels preserve unknown codes");
    var emojiFile = Path.Combine(temp, "emoji-seen.json"); var emojiQueue = new EmojiReactions(emojiFile);
    Check(emojiQueue.Incoming("server:user2", emojiMessage, true) && emojiQueue.Count == 3, "incoming message schedules at most three emoji reactions");
    Check(!emojiQueue.Incoming("server:user2", emojiMessage, true), "repeated message delivery does not repeat reaction");
    Check(!new EmojiReactions(emojiFile).Incoming("server:user2", emojiMessage, true), "reaction deduplication survives client restart");
    Check(new EmojiReactions(emojiFile).Incoming("server:user3", emojiMessage, true), "emoji state remains isolated by authenticated user");
    emojiQueue.Clear(); for (int i = 0; i < 20; i++) emojiQueue.Insert(HelperEmojis.All[0]); Check(emojiQueue.Count == 9, "emoji reaction queue bounded during bursts");
    emojiQueue.Clear(); emojiQueue.Insert(HelperEmojis.All[0], DateTime.UtcNow.AddMinutes(-1)); Check(emojiQueue.Take(DateTime.UtcNow) == null, "stale deferred emoji reactions expire");
    var quietQueue = new EmojiReactions(); Check(!quietQueue.Incoming("quiet", emojiMessage, false) && quietQueue.Count == 0 && !quietQueue.Incoming("quiet", emojiMessage, true), "suppressed reactions are not replayed when notifications resume");
    var chatOrder = ChatContact.Ordered(new[] {
        new ChatContact(1, "Личный чат", "one", true, null, 1, 8, true, DateTime.UtcNow.AddMinutes(-2)),
        new ChatContact(2, "Прочитанный чат", "two", true, null, 0, 50, true, DateTime.UtcNow),
        new ChatContact(-3, "Группа", "", true, null, 3, 1, false, DateTime.UtcNow.AddMinutes(-1), "Новый ответ", true)
    }).ToArray();
    Check(chatOrder.Select(c => c.Id).SequenceEqual(new[] { -3, 1, 2 }), "unread groups and private chats lead list sorted by message time");
    Check(fresh.EnsureClientKey() && Convert.FromBase64String(fresh.ClientToken).Length == 48, "client generates cryptographic registration key");
    string generated = fresh.ClientToken;
    Check(!fresh.EnsureClientKey() && fresh.ClientToken == generated, "generated registration key reused");
    other.EnsureClientKey(); Check(other.ClientToken != generated, "independent clients get different keys");
    string manifestPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "release-1.2.0", "PixelHelper.update.json"));
    if (File.Exists(manifestPath))
    {
        var manifest = JsonSerializer.Deserialize<PixelHelper.Updates.ClientUpdateManifest>(File.ReadAllText(manifestPath), Settings.Json)!;
        Check(manifest.Valid(), "client accepts trusted signed update manifest");
        Check(!(manifest with { Sha256 = new string('0', 64) }).Valid(), "client rejects altered update checksum");
        Check(!(manifest with { Signature = "invalid" }).Valid(), "client rejects unsigned or invalid update manifest");
        Check(!(manifest with { Size = PixelHelper.Updates.ClientUpdateManifest.MaximumSize + 1 }).Valid(), "client refuses oversized MSI update");
    }
    var installedSettings = new Settings { ServerUrl = "http://old-server:5000", ClientToken = "test-key", X = 42, HubUrl = "https://explicit.example/helperHub" };
    Settings.ApplyInstalledServer(installedSettings, "https://new-server.example:5443/team/");
    Check(installedSettings.ServerUrl == "https://new-server.example:5443/team" && installedSettings.ClientToken == "test-key" && installedSettings.X == 42 && installedSettings.HubUrl == "https://explicit.example/helperHub", "installed server replaces saved address without changing user settings or explicit hub");
    foreach (string invalid in new[] { "", "file:///C:/server", "not a URL", "http:\\\\172.16.16.61,", "http://172.16.16.61,", "http://helper.gp1.loc ", "https://user:pass@server", "https://server/?token=secret", "https://server/#fragment" })
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
    Check(ApiClient.Defaults().Select(a => a.Id).SequenceEqual(new[] { "search", "dmed", "eisz", "exit" }), "offline menu excludes startup disabling");
    Check(api.ReadCache().Count == 4, "offline defaults");
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
    Check(api.ReadCache().Count == 4, "corrupt cache fallback");
    var disabledExecutor = new CommandExecutor(new Settings { EnableAdministrativeCommands = false });
    var processExecutor = new CommandExecutor(new Settings());
    var processesResult = await processExecutor.ExecuteAsync(new("process-list", "processes", ""), _ => Task.CompletedTask, CancellationToken.None);
    var processes = JsonSerializer.Deserialize<ProcessEntry[]>(processesResult.Output, Settings.Json)!;
    Check(processesResult.ExitCode == 0 && processes.Any(p => p.Pid == Environment.ProcessId && !p.CanStop), "process list includes current process and prevents stopping helper");
    var sleeperStart = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
    foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 60" }) sleeperStart.ArgumentList.Add(argument);
    using (var sleeper = System.Diagnostics.Process.Start(sleeperStart)!)
    try
    {
        var entry = ProcessInspector.Collect().Single(p => p.Pid == sleeper.Id);
        Check(entry.CanStop && entry.StartTimeUtcTicks != null, "owned test process has selectable PID identity");
        var stale = await processExecutor.ExecuteAsync(new("stale", "kill_pid", JsonSerializer.Serialize(new ProcessTarget(entry.Pid, entry.Name, "1"), Settings.Json)), _ => Task.CompletedTask, CancellationToken.None);
        Check(stale.ExitCode == 87 && !sleeper.HasExited, "stale process identity rejected without stopping process");
        var stopped = await processExecutor.ExecuteAsync(new("stop", "kill_pid", JsonSerializer.Serialize(new ProcessTarget(entry.Pid, entry.Name, entry.StartTimeUtcTicks!), Settings.Json)), _ => Task.CompletedTask, CancellationToken.None);
        Check(stopped.ExitCode == 0 && sleeper.WaitForExit(5000), "selected test process stopped by PID");
        var gone = await processExecutor.ExecuteAsync(new("gone", "kill_pid", JsonSerializer.Serialize(new ProcessTarget(entry.Pid, entry.Name, entry.StartTimeUtcTicks!), Settings.Json)), _ => Task.CompletedTask, CancellationToken.None);
        Check(gone.ExitCode != 0, "exited process reports failure rather than hanging task");
    }
    finally { if (!sleeper.HasExited) { sleeper.Kill(); sleeper.WaitForExit(); } }
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
    for (int index = 0; index < Sprites.FrameCount(state); index++)
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
    pet.SetDisplayMode("Topmost"); Check(pet.Topmost, "topmost display mode enabled");
    pet.SetDisplayMode("Normal"); Check(!pet.Topmost, "normal display mode clears topmost");
    pet.SetDisplayMode("Background");
    pet.SetAssistantHidden(true); Check(!pet.IsVisible, "helper hides without terminating window"); pet.SetAssistantHidden(false); Check(pet.IsVisible, "helper can be restored from tray action");
    foreach (var mode in new[] { "Background", "Normal", "Topmost" }) {
        pet.SetDisplayMode(mode); pet.SetAssistantHidden(true);
        pet.ReceiveAnnouncement(new ClientNotice("Срочная проверка", "Тест", 60) { UrgentMessageId = 987, ExpiresAt = DateTime.UtcNow.AddHours(1) });
        Check(pet.IsVisible && pet.Topmost, "urgent notice overrides hidden " + mode + " mode");
        typeof(PetWindow).GetField("announcementUntil",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(pet,DateTime.UtcNow.AddSeconds(-1));pet.AdvanceAnnouncements();
        Check(!pet.IsVisible && pet.Topmost == (mode == "Topmost"), "urgent timeout restores hidden " + mode + " preference");
        pet.SetAssistantHidden(false);
    }
    pet.SetDisplayMode("Background");
    typeof(PetWindow).GetMethod("ChangeState",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(pet,new object[] { PetState.Idle });
    var trayHandle = new System.Windows.Interop.WindowInteropHelper(pet).Handle;
    using (var nativeTray = new TrayIcon(trayHandle, new Sprites().Get(PetState.Idle, 0), () => { }, () => { }, _ => { }))
    { Check(nativeTray.Registered || System.Runtime.InteropServices.Marshal.GetLastWin32Error() == -2147467259, "native tray icon registered with Explorer"); nativeTray.SetUnread(true); nativeTray.SetUnread(false); }
    var petFlags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    var petSettings = (Settings)typeof(PetWindow).GetField("settings", petFlags)!.GetValue(pet)!; petSettings.EmojiReactions = true; petSettings.ChatDoNotDisturb = false;
    var petStateField = typeof(PetWindow).GetField("state", petFlags)!; var advanceEmoji = typeof(PetWindow).GetMethod("AdvanceEmoji", petFlags)!;
    pet.InsertEmojiReaction(HelperEmojis.All[1]); Check((PetState)petStateField.GetValue(pet)! == PetState.Joy, "inserting emoji starts matching helper animation");
    pet.React(PetState.Error, 4); pet.InsertEmojiReaction(HelperEmojis.All[5]); Check((PetState)petStateField.GetValue(pet)! == PetState.Error, "important error animation takes priority over emoji");
    typeof(PetWindow).GetField("actionUntil", petFlags)!.SetValue(pet, DateTime.UtcNow.AddSeconds(-1)); advanceEmoji.Invoke(pet, null); Check((PetState)petStateField.GetValue(pet)! == PetState.Laugh, "deferred emoji plays when important animation ends");
    typeof(PetWindow).GetField("emojiUntil", petFlags)!.SetValue(pet, DateTime.UtcNow.AddSeconds(-1)); advanceEmoji.Invoke(pet, null); Check((PetState)petStateField.GetValue(pet)! == PetState.Idle, "helper returns to idle after emoji reaction");
    typeof(PetWindow).GetMethod("ChangeState", petFlags)!.Invoke(pet, new object[] { PetState.Sleep }); pet.InsertEmojiReaction(HelperEmojis.All[0]);
    typeof(PetWindow).GetField("emojiUntil", petFlags)!.SetValue(pet, DateTime.UtcNow.AddSeconds(-1)); advanceEmoji.Invoke(pet, null); Check((PetState)petStateField.GetValue(pet)! == PetState.Sleep, "sleeping helper returns to sleep after emoji");
    typeof(PetWindow).GetMethod("ChangeState", petFlags)!.Invoke(pet, new object[] { PetState.Idle });

    // Single emoji reactions
    var checkEmojiMethod = typeof(PetWindow).GetMethod("CheckSingleEmojiReaction", petFlags)!;
    var faceStatusField = typeof(PetWindow).GetField("faceStatus", petFlags)!;
    bool heartHandled = (bool)checkEmojiMethod.Invoke(pet, new object[] { "❤️" })!;
    Check(heartHandled && (FaceNoticeStatus)faceStatusField.GetValue(pet)! == FaceNoticeStatus.Heart, "single heart emoji activates pulsing heart on face");
    bool laughHandled = (bool)checkEmojiMethod.Invoke(pet, new object[] { "😁" })!;
    Check(laughHandled && (PetState)petStateField.GetValue(pet)! == PetState.Laugh, "single laugh emoji activates laugh animation");
    bool facepalmHandled = (bool)checkEmojiMethod.Invoke(pet, new object[] { "🤦‍♂️" })!;
    Check(facepalmHandled && (PetState)petStateField.GetValue(pet)! == PetState.Facepalm, "single facepalm emoji activates facepalm animation");
    bool mixedHandled = (bool)checkEmojiMethod.Invoke(pet, new object[] { "Привет ❤️" })!;
    Check(!mixedHandled, "mixed text with emoji does not trigger single emoji reaction");
    pet.ShowFaceMail(3);
    Check((FaceNoticeStatus)faceStatusField.GetValue(pet)! == FaceNoticeStatus.Mail, "ShowFaceMail sets face status to Mail");

    Check(pet.InputHitTest(new System.Windows.Point(48, 32)) is System.Windows.Controls.Image, "robot receives WPF input after canvas translation");
    typeof(PetWindow).GetMethod("ShowMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(pet, null);
    pet.UpdateLayout();
    Check(pet.ActualWidth == 500 && pet.ActualHeight == 400, "expanded menu layout");
    Check(pet.InputHitTest(new System.Windows.Point(250, 200)) is System.Windows.Controls.Image, "robot input remains correct with menu open");
    var menuCanvas = (System.Windows.Controls.Canvas)pet.Content;
    var menuButtons = menuCanvas.Children.OfType<System.Windows.Controls.Button>().ToList();
    Check(menuButtons.Count > 0, "radial menu has buttons");
    var firstBtn = menuButtons[0];
    double firstBtnCenterX = System.Windows.Controls.Canvas.GetLeft(firstBtn) + firstBtn.Width / 2.0;
    double firstBtnCenterY = System.Windows.Controls.Canvas.GetTop(firstBtn) + firstBtn.Height / 2.0;
    Check(firstBtnCenterX < 250, "first radial button starts strictly on the left of robot");
    Check(Math.Abs(firstBtnCenterY - 200) < 40, "first radial button vertically aligns near robot center");
    foreach (var child in menuCanvas.Children.OfType<System.Windows.UIElement>()) { child.BeginAnimation(System.Windows.UIElement.OpacityProperty, null); child.Opacity = 1; }
    var menuBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(500, 400, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); menuBitmap.Render(menuCanvas);
    var menuPreview = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "menu-preview.png")); Directory.CreateDirectory(Path.GetDirectoryName(menuPreview)!);
    var menuPng = new System.Windows.Media.Imaging.PngBitmapEncoder(); menuPng.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(menuBitmap)); using (var menuImage = File.Create(menuPreview)) menuPng.Save(menuImage);
    typeof(PetWindow).GetMethod("HideBubbles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(pet, null);
    pet.UpdateLayout();
    Check(pet.ActualWidth == 96 && pet.InputHitTest(new System.Windows.Point(48, 32)) is System.Windows.Controls.Image, "menu collapse restores robot hit testing");
    double taskbarAnchor = System.Windows.SystemParameters.WorkArea.Bottom - 96;
    pet.Top = taskbarAnchor;
    typeof(PetWindow).GetMethod("Expand", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(pet, null);
    typeof(PetWindow).GetMethod("Collapse", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(pet, null);
    Check(Math.Abs(pet.Top - taskbarAnchor) < 1, "resize cycle preserves robot anchor beside taskbar");
    Check(pet.ReceiveAnnouncement(new("Проверка сообщения\nПомощник показывает текст в облачке над собой.", "Супер администратор", 30)).Contains("показано"), "notice displayed on WPF dispatcher");
    pet.UpdateLayout();
    var canvas = (System.Windows.Controls.Canvas)pet.Content;
    var bubble = canvas.Children.OfType<System.Windows.Controls.Border>().Single();
    Check(System.Windows.Controls.Canvas.GetTop(bubble) + bubble.Height <= 286 && canvas.Children.OfType<System.Windows.Shapes.Polygon>().Any(), "comic bubble and tail are above robot");
    foreach(var child in canvas.Children.OfType<System.Windows.UIElement>()) { child.BeginAnimation(System.Windows.UIElement.OpacityProperty,null); child.Opacity=1; }
    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(500,400,96,96,System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(canvas);
    var preview=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","artifacts","notification-preview.png")); Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
    var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var imageFile=File.Create(preview))png.Save(imageFile);
    typeof(PetWindow).GetField("announcementUntil",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(pet,DateTime.UtcNow.AddSeconds(-1));pet.AdvanceAnnouncements();pet.UpdateLayout();
    Check(pet.ActualWidth==96,"announcement expires and restores compact helper");
    pet.ShowClickReaction(RobotClickReaction.Greeting); pet.UpdateLayout();
    Check((PetState)petStateField.GetValue(pet)! == PetState.Twirl && Sprites.FrameCount(PetState.Twirl)==8,"greeting starts an eight-frame full turn");
    pet.ShowClickReaction(RobotClickReaction.Offended); pet.UpdateLayout();
    Check((PetState)petStateField.GetValue(pet)! == PetState.Offended && pet.ActualWidth==500,"offended animation is shown together with comic message");
    Check(!new Sprites().Get(PetState.Offended,0).Pixels.SequenceEqual(new Sprites().Get(PetState.Offended,1).Pixels),"offended animation has distinct blinking frames");
    typeof(PetWindow).GetField("announcementUntil",petFlags)!.SetValue(pet,DateTime.UtcNow.AddSeconds(-1));pet.AdvanceAnnouncements();pet.UpdateLayout();
    Check(pet.ActualWidth==96,"offended comic expires without user action");
    pet.ReceiveAnnouncement(new("Первое", "Администратор", 30));
    for(int n=0;n<10;n++)Check(pet.ReceiveAnnouncement(new("Очередь "+n,"Администратор",30)).Contains("очередь"),"queued notice "+n);
    bool full=false;try{pet.ReceiveAnnouncement(new("Лишнее","Администратор",30));}catch(InvalidOperationException){full=true;}Check(full,"notice queue bounded to ten pending messages");
    var ticketWindow = new TicketWindow((title, description, branch, room, cancellation) => Task.FromResult(1), cancellation => Task.FromResult(new List<TicketBranch> { new(1, "Поликлиника"), new(2, "Больница") }), new Settings { TicketBranchId = 1, TicketRoom = "12" }, CancellationToken.None);
    Check(ticketWindow.WindowStartupLocation == System.Windows.WindowStartupLocation.CenterScreen, "ticket window startup location is CenterScreen");
    ticketWindow.Show(); ticketWindow.UpdateLayout(); ticketWindow.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    var ticketPanel = (System.Windows.Controls.StackPanel)((System.Windows.Controls.Border)ticketWindow.Content).Child;
    Check(ticketPanel.Children.OfType<System.Windows.Controls.ComboBox>().Single().SelectedValue is int selectedBranch && selectedBranch == 1, "ticket form restores saved branch after directory load");
    Check(ticketPanel.Children.OfType<System.Windows.Controls.TextBox>().First().Text == "12", "ticket form restores saved room");
    var ticketBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(410, 410, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); ticketBitmap.Render(ticketWindow);
    var ticketPreview = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "ticket-preview.png"));
    var ticketPng = new System.Windows.Media.Imaging.PngBitmapEncoder(); ticketPng.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(ticketBitmap)); using (var ticketImage = File.Create(ticketPreview)) ticketPng.Save(ticketImage);
    ticketWindow.Close();
    var chatClient = new MessengerClient(new Settings { ServerUrl = "http://helper.gp1.loc" });
    var chatWindow = new MessengerWindow(chatClient, new Settings());
    chatWindow.Show(); chatWindow.UpdateLayout(); chatWindow.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    Check(chatWindow.ActualWidth >= 720 && ((System.Windows.Controls.Grid)chatWindow.Content).Children.Count > 0, "messenger automatic Windows sign-in view is displayed");
    Check(!((System.Windows.Controls.Grid)chatWindow.Content).Children.OfType<System.Windows.Controls.StackPanel>().SelectMany(p => p.Children.Cast<System.Windows.UIElement>()).Any(c => c is System.Windows.Controls.PasswordBox or System.Windows.Controls.TextBox), "automatic sign-in has no login or password inputs");
    var chatBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)chatWindow.ActualWidth, (int)chatWindow.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); chatBitmap.Render(chatWindow);
    var chatPreview = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "messenger-login-preview.png"));
    var chatPng = new System.Windows.Media.Imaging.PngBitmapEncoder(); chatPng.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(chatBitmap)); using (var chatImage = File.Create(chatPreview)) chatPng.Save(chatImage);
    typeof(MessengerWindow).GetMethod("MainForm", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(chatWindow, null);
    var composerField = (System.Windows.Controls.TextBox)typeof(MessengerWindow).GetField("input", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(chatWindow)!;
    composerField.Text = "До после"; composerField.Select(3, 0); int insertedEmojis = 0; chatWindow.EmojiInserted += _ => insertedEmojis++;
    chatWindow.InsertEmoji(HelperEmojis.All[0]); Check(composerField.Text == "До :helper_wave:после" && insertedEmojis == 1, "emoji inserted at caret with one immediate reaction event");
    var renderedEmoji = HelperEmojis.Render("Текст :helper_wave: :helper_party: 😄 :helper_unknown:");
    Check(renderedEmoji.Inlines.OfType<System.Windows.Documents.InlineUIContainer>().Count() == 2, "known emoji displayed inline as robot images");
    var animated = HelperEmojis.AnimatedImage(HelperEmojis.All[0], 32); var still = HelperEmojis.AnimatedImage(HelperEmojis.All[0], 32, false);
    var animationPanel = new System.Windows.Controls.StackPanel(); animationPanel.Children.Add(animated); animationPanel.Children.Add(still);
    var animationWindow = new System.Windows.Window { Content = animationPanel, Width = 120, Height = 160 }; animationWindow.Show();
    var firstFrame = animated.Source; var staticFrame = still.Source;
    var dispatcherFrame = new System.Windows.Threading.DispatcherFrame(); var stop = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
    stop.Tick += (_, _) => { stop.Stop(); dispatcherFrame.Continue = false; }; stop.Start(); System.Windows.Threading.Dispatcher.PushFrame(dispatcherFrame);
    Check(!ReferenceEquals(firstFrame, animated.Source) && ReferenceEquals(staticFrame, still.Source), "visible emoji animates while disabled emoji remains static"); animationWindow.Close();
    typeof(MessengerWindow).GetField("contacts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(chatWindow, new List<ChatContact> { new(1, "Анна Иванова", "anna@gp1.loc", true, "Поликлиника", 2, 1, true, DateTime.UtcNow.AddMinutes(-2), "Подскажите расписание"), new(2, "Борис Петров", "boris@gp1.loc", true, "Больница", 0, null, false), new(-3, "Отдел информационных технологий", "", true, null, 4, 2, false, DateTime.UtcNow, "Коллеги, обновление установлено", true, 0) });
    typeof(MessengerWindow).GetMethod("Filter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(chatWindow, null);
    typeof(MessengerWindow).GetField("messages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(chatWindow, new List<ChatEntry> { new(1, 1, 0, "Добрый день! Подскажите, пожалуйста, как найти расписание приёма?", "first", DateTime.UtcNow, null), new(2, 0, 1, "Здравствуйте! Отправлю вам ссылку на расписание.", "second", DateTime.UtcNow, DateTime.UtcNow) });
    typeof(MessengerWindow).GetMethod("RenderHistory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(chatWindow, new object[] { true });
    chatWindow.UpdateLayout();
    Check(((System.Windows.Controls.Grid)chatWindow.Content).ColumnDefinitions.Count == 4, "messenger provides activity bar, directory, conversation and optional information panes");
    Check(MessengerDialog.Matches(new(1,"Анна Иванова","anna@gp1.loc",true,"Поликлиника",0,null,false), "иван") && MessengerDialog.Matches(new(1,"Анна Иванова","anna@gp1.loc",true,"Поликлиника",0,null,false),"клиника"), "group search matches phrases anywhere in name or branch");
    var mainBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)chatWindow.ActualWidth, (int)chatWindow.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); mainBitmap.Render(chatWindow); var mainPng = new System.Windows.Media.Imaging.PngBitmapEncoder(); mainPng.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(mainBitmap)); using (var mainImage = File.Create(chatPreview.Replace("login", "dialogue"))) mainPng.Save(mainImage);
    typeof(MessengerWindow).GetField("peer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(chatWindow, -3);
    typeof(MessengerWindow).GetField("messages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(chatWindow, new List<ChatEntry> { new(1, 1, -3, "Коллеги, привет! :helper_wave: :helper_joy:", "group1", DateTime.UtcNow, null, "Анна Иванова"), new(2, 0, -3, "Спасибо! Обновление работает :helper_thanks: :helper_party:", "group2", DateTime.UtcNow, null, "Артём Шпынов", [new("preview-image", "Image.png", 2048), new("preview-document", "Document.pdf", 15360)]) });
    typeof(MessengerWindow).GetMethod("RenderHistory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(chatWindow, new object[] { true }); chatWindow.UpdateLayout();
    var groupBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)chatWindow.ActualWidth, (int)chatWindow.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); groupBitmap.Render(chatWindow); var groupPng = new System.Windows.Media.Imaging.PngBitmapEncoder(); groupPng.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(groupBitmap)); using (var groupImage = File.Create(chatPreview.Replace("login", "groups"))) groupPng.Save(groupImage);
    var appearanceSettings = (Settings)typeof(MessengerWindow).GetField("settings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(chatWindow)!;
    foreach (var theme in new[] { "Helper", "Light", "Dark", "Contrast" })
    {
        appearanceSettings.ChatTheme = theme; appearanceSettings.ChatFontSize = 20;
        typeof(MessengerWindow).GetMethod("ApplyTheme", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(chatWindow, null);
        typeof(MessengerWindow).GetMethod("MainForm", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(chatWindow, null);
        typeof(MessengerWindow).GetMethod("Filter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(chatWindow, null);
        typeof(MessengerWindow).GetMethod("RenderHistory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(chatWindow, new object[] { true }); chatWindow.UpdateLayout();
        Check(chatWindow.FontSize == 20, theme + " theme renders with accessible large text");
        var preferences = new MessengerPreferences(appearanceSettings); preferences.Show(); preferences.UpdateLayout();
        var preferencesPanel = (System.Windows.Controls.StackPanel)((System.Windows.Controls.ScrollViewer)preferences.Content).Content;
        foreach (var toggle in preferencesPanel.Children.OfType<System.Windows.Controls.CheckBox>())
            Check(toggle.Foreground.ToString() == preferences.Foreground.ToString(), theme + " checkbox text uses theme foreground");
        foreach (var choice in preferencesPanel.Children.OfType<System.Windows.Controls.ComboBox>())
            Check(choice.Foreground.ToString() == preferences.Foreground.ToString() && choice.Template.FindName("DropDown", choice) is System.Windows.Controls.Primitives.Popup, theme + " themed choice template applied");
        var preferencesBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)preferences.ActualWidth, (int)preferences.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); preferencesBitmap.Render(preferences);
        var preferencesPng = new System.Windows.Media.Imaging.PngBitmapEncoder(); preferencesPng.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(preferencesBitmap)); using (var preferencesFile = File.Create(chatPreview.Replace("login", "settings-" + theme))) preferencesPng.Save(preferencesFile);
        preferences.Close();
        var themedBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)chatWindow.ActualWidth, (int)chatWindow.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); themedBitmap.Render(chatWindow);
        var themedPng = new System.Windows.Media.Imaging.PngBitmapEncoder(); themedPng.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(themedBitmap)); using var themedFile = File.Create(chatPreview.Replace("login", theme)); themedPng.Save(themedFile);
    }
    string attachmentPath = Path.Combine(temp, "sample.unknown"); File.WriteAllBytes(attachmentPath, [1, 2, 3]);
    chatWindow.AddFiles([attachmentPath]);
    var pendingAttachments = (List<string>)typeof(MessengerWindow).GetField("pendingFiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(chatWindow)!;
    Check(pendingAttachments.Count == 1, "composer accepts arbitrary file extension");
    var imagePixels = new byte[16 * 16 * 4]; Array.Fill<byte>(imagePixels, 255);
    chatWindow.AttachImage(System.Windows.Media.Imaging.BitmapSource.Create(16, 16, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, imagePixels, 16 * 4));
    Check(pendingAttachments.Count == 2 && File.ReadAllBytes(pendingAttachments[1]).Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "pasted image encoded as PNG attachment");
    string tooLarge = Path.Combine(temp, "oversize.bin"); using (var large = File.Create(tooLarge)) large.SetLength(50 * 1024 * 1024 + 1);
    chatWindow.AddFiles([tooLarge]); Check(pendingAttachments.Count == 2, "oversized selection leaves existing attachments intact");
    string pastedFile = pendingAttachments[1]; chatWindow.Close(); Check(!File.Exists(pastedFile) && File.Exists(attachmentPath), "window cleanup removes only owned temporary images"); chatClient.DisposeAsync().AsTask().GetAwaiter().GetResult();

    // SoundManager and Audio assets verification
    Check(SoundManager.NormalizeProfile("Sound") == "Sound", "sound profile normalization Sound");
    Check(SoundManager.NormalizeProfile("VoiceAdult") == "VoiceAdult", "sound profile normalization VoiceAdult");
    Check(SoundManager.NormalizeProfile("VoiceChild") == "VoiceChild", "sound profile normalization VoiceChild");
    Check(SoundManager.NormalizeProfile("unknown") == "Sound", "sound profile fallback to Sound");

    string assetsSoundDir = SoundManager.GetSoundBaseDir();
    string[] soundProfiles = ["Sound", "VoiceAdult", "VoiceChild"];
    string[] soundEvents = ["send.wav", "receive.wav", "dance.wav", "urgent.wav"];
    foreach (var prof in soundProfiles)
    {
        foreach (var ev in soundEvents)
        {
            string wavPath = Path.Combine(assetsSoundDir, prof, ev);
            Check(File.Exists(wavPath) && new FileInfo(wavPath).Length > 1000, $"audio asset {prof}/{ev} exists and non-empty");
        }
    }

    settings.ChatSound = true;
    SoundManager.Play(SoundEvent.MessageSent, settings);
    SoundManager.Play(SoundEvent.MessageReceived, settings);
    SoundManager.Play(SoundEvent.Dance, settings);
    SoundManager.Play(SoundEvent.Urgent, settings);
    SoundManager.PlayPreview("VoiceAdult", SoundEvent.MessageReceived);
    SoundManager.PlayPreview("VoiceChild", SoundEvent.MessageSent);
    Check(true, "SoundManager plays events safely without exceptions");

    // Dance lead delay verification
    Check(SoundManager.GetDanceLeadDelayMs("VoiceAdult") == 2200, "VoiceAdult dance lead delay is 2200ms");
    Check(SoundManager.GetDanceLeadDelayMs("VoiceChild") == 0, "VoiceChild dance lead delay is 0ms");
    Check(SoundManager.GetDanceLeadDelayMs("Sound") == 0, "Sound dance lead delay is 0ms");

    // Multilingual audio packs verification
    string[] langs = ["ru", "kk", "en", "zh"];
    foreach (var lang in langs)
    {
        Check(File.Exists(Path.Combine(assetsSoundDir, "VoiceAdult", lang, "send.wav")), $"VoiceAdult/{lang}/send.wav exists");
        Check(File.Exists(Path.Combine(assetsSoundDir, "VoiceAdult", lang, "dance.wav")), $"VoiceAdult/{lang}/dance.wav exists");
        Check(File.Exists(Path.Combine(assetsSoundDir, "VoiceChild", lang, "receive_1.wav")), $"VoiceChild/{lang}/receive_1.wav exists");
        Check(File.Exists(Path.Combine(assetsSoundDir, "VoiceChild", lang, "receive_2.wav")), $"VoiceChild/{lang}/receive_2.wav exists");
        Check(File.Exists(Path.Combine(assetsSoundDir, "VoiceChild", lang, "urgent.wav")), $"VoiceChild/{lang}/urgent.wav exists");
    }

    // CompanionStorage tests
    string testCompanionDb = Path.Combine(temp, "companion_test.dat");
    CompanionStorage.SetCustomFilePathForTesting(testCompanionDb);
    CompanionStorage.Clear();
    Check(!File.Exists(testCompanionDb), "CompanionStorage.Clear removes file");
    Check(CompanionStorage.Load().Count == 0, "CompanionStorage.Load on empty returns empty list");

    CompanionStorage.Append(new CompanionMessage(DateTime.UtcNow.AddDays(-40), true, "Old message", "Normal"));
    CompanionStorage.Append(new CompanionMessage(DateTime.UtcNow, true, "Recent message", "Normal"));
    Check(CompanionStorage.Load().Count == 2, "CompanionStorage loads saved messages");
    int purged = CompanionStorage.AutoPurge(TimeSpan.FromDays(30));
    Check(purged == 1 && CompanionStorage.Load().Count == 1, "CompanionStorage.AutoPurge deletes messages older than 30 days");
    CompanionStorage.Clear();
    Check(CompanionStorage.Load().Count == 0, "CompanionStorage clear after test leaves 0 messages");
    CompanionStorage.SetCustomFilePathForTesting(null);

    // CompanionBotEngine age formatting tests
    var origin = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    TimeSpan age8Days = TimeSpan.FromDays(8);
    Check(CompanionBotEngine.FormatAge(age8Days, "ru").Contains("8 дней"), "age formatting in RU for 8 days");
    Check(CompanionBotEngine.FormatAge(age8Days, "kk").Contains("8 күн"), "age formatting in KK for 8 days");
    Check(CompanionBotEngine.FormatAge(age8Days, "en").Contains("8 days"), "age formatting in EN for 8 days");
    Check(CompanionBotEngine.FormatAge(age8Days, "zh").Contains("8天"), "age formatting in ZH for 8 days");

    // CompanionBotEngine insult and cry reaction
    Loc.Initialize("ru");
    var insultRes = CompanionBotEngine.GenerateResponse("Ты тупой робот", origin);
    Check(insultRes.Emotion == CompanionEmotion.Cry, "insult triggers cry emotion");
    Check(insultRes.Action == CompanionBotAction.CryReaction, "insult sets cry action");
    Check(insultRes.ReplyText.Contains("Пожалуйста не обижайте меня") && insultRes.ReplyText.Contains("Мне всего"), "insult reply contains hurt phrase and age in RU");

    // CompanionBotEngine forget command
    var forgetRes = CompanionBotEngine.GenerateResponse("Забудь всё о чём мы говорили");
    Check(forgetRes.Action == CompanionBotAction.ClearHistory, "forget command sets ClearHistory action");
    Check(forgetRes.ReplyText.Contains("чистый лист"), "forget reply contains clean slate phrase in RU");

    // CompanionBotEngine empathy categories
    var sadRes = CompanionBotEngine.GenerateResponse("Мне очень грустно и одиноко");
    Check(sadRes.Emotion == CompanionEmotion.Sad, "sadness triggers Sad emotion");
    var tiredRes = CompanionBotEngine.GenerateResponse("Я так сильно устал на работе");
    Check(tiredRes.Emotion == CompanionEmotion.Think, "fatigue triggers Think emotion");
    var joyRes = CompanionBotEngine.GenerateResponse("Ура, у меня всё получилось!");
    Check(joyRes.Emotion == CompanionEmotion.Joy, "joy triggers Joy emotion");
    var jokeRes = CompanionBotEngine.GenerateResponse("Расскажи шутку или анекдот");
    Check(jokeRes.Emotion == CompanionEmotion.Laugh, "joke triggers Laugh emotion");
    var unknownRes = CompanionBotEngine.GenerateResponse("Квантовая хромодинамика в теории струн");
    Check(unknownRes.ReplyText.Contains("Меня этому не научили"), "unknown query returns fallback phrase");

    // ActionButton translation support
    var btn = new ActionButton(1, "Поиск", "search", "open_url", "https://google.com", 0, true, "All",
        new Dictionary<string, string> { ["ru"] = "Поиск", ["kk"] = "Іздеу", ["en"] = "Search", ["zh"] = "搜索" });
    Check(btn.GetLocalizedTitle("ru") == "Поиск", "button localized title ru");
    Check(btn.GetLocalizedTitle("kk") == "Іздеу", "button localized title kk");
    Check(btn.GetLocalizedTitle("en") == "Search", "button localized title en");
    Check(btn.GetLocalizedTitle("zh") == "搜索", "button localized title zh");

    // Disk space monitor free percent check
    double? freeSpacePercent = DiskSpaceMonitor.GetFreePercent("C");
    Check(freeSpacePercent.HasValue && freeSpacePercent.Value > 0 && freeSpacePercent.Value <= 100, "DiskSpaceMonitor reads C: free space percentage");

    // Birthday hat asset verification
    string hatFile = Path.Combine(AppContext.BaseDirectory, "Assets", "hat_birthday.png");
    if (!File.Exists(hatFile)) hatFile = Path.Combine(Environment.CurrentDirectory, "LitleHelperClient", "Assets", "hat_birthday.png");
    Check(File.Exists(hatFile) && new FileInfo(hatFile).Length > 100, "birthday hat asset exists and non-empty");

    // Liquidation Farewell window and SelfDestruct tests
    Loc.Initialize("ru");
    Check(Loc.T("FarewellLiquidationNotice").Contains("Мне очень жаль...😭… Папа забирает меня..."), "RU farewell phrase match");
    Loc.Initialize("kk");
    Check(Loc.T("FarewellLiquidationNotice").Contains("Өкінішке орай...😭… Әкем мені алып кетіп бара жатыр..."), "KK farewell phrase match");
    Loc.Initialize("en");
    Check(Loc.T("FarewellLiquidationNotice").Contains("I'm so sorry...😭… Papa is taking me away..."), "EN farewell phrase match");
    Loc.Initialize("zh");
    Check(Loc.T("FarewellLiquidationNotice").Contains("真的很抱歉……😭……爸爸要带我走了……"), "ZH farewell phrase match");
    Loc.Initialize("ru");

    var farewell = new FarewellLiquidationWindow(durationSeconds: 120, autoSelfDestruct: false);
    Check(!farewell.CanCloseNow, "farewell window cannot be closed initially");
    Check(farewell.RemainingSeconds == 120, "farewell window initialized with 120s countdown");
    bool closingCancelled = false;
    farewell.Closing += (_, e) => { if (e.Cancel) closingCancelled = true; };
    farewell.Close();
    Check(closingCancelled && !farewell.CanCloseNow, "closing attempt cancelled while countdown active");

    string cleanupScript = SelfDestructManager.GenerateCleanupScript(9999, @"C:\App\PixelHelper.exe", @"C:\App\Data");
    Check(cleanupScript.Contains("9999") && cleanupScript.Contains("taskkill") && cleanupScript.Contains("rmdir"), "cleanup script generated with PID and cleanup commands");

    // Section 22: Password Policy Validator tests
    Check(!PasswordPolicyValidator.Validate("short").IsValid, "password policy rejects < 8 chars");
    Check(PasswordPolicyValidator.Validate("short").Message.Contains("8"), "short password mentions 8 chars");
    Check(!PasswordPolicyValidator.Validate("nouppercase1!").IsValid, "password policy rejects missing uppercase");
    Check(PasswordPolicyValidator.Validate("nouppercase1!").Message.Contains("заглавные"), "missing uppercase mentions заглавные");
    Check(!PasswordPolicyValidator.Validate("NOLOWERCASE1!").IsValid, "password policy rejects missing lowercase");
    Check(PasswordPolicyValidator.Validate("NOLOWERCASE1!").Message.Contains("строчные"), "missing lowercase mentions строчные");
    Check(!PasswordPolicyValidator.Validate("NoDigitsHere!").IsValid, "password policy rejects missing digit");
    Check(PasswordPolicyValidator.Validate("NoDigitsHere!").Message.Contains("цифры"), "missing digit mentions цифры");
    Check(PasswordPolicyValidator.Validate("KzAdmin2026!").IsValid, "password policy accepts valid complex password");
    Check(PasswordPolicyValidator.Validate("Solnce2026").IsValid, "password policy accepts valid alphanumeric password");

    // Section 22: Mnemonic Password Generator tests
    string singleGen = MnemonicPasswordGenerator.GenerateOne();
    Check(!string.IsNullOrWhiteSpace(singleGen) && singleGen.Length >= 8, "Mnemonic generator generates non-empty >= 8 char password");
    Check(PasswordPolicyValidator.Validate(singleGen).IsValid, "Mnemonic generator produces password satisfying policy");
    var multipleGen = MnemonicPasswordGenerator.Generate(10);
    Check(multipleGen.Count == 10 && multipleGen.All(p => PasswordPolicyValidator.Validate(p).IsValid), "All 10 generated mnemonic passwords satisfy policy");

    // Section 22: Companion Bot Password Dialogue flow tests
    Loc.Initialize("ru");
    var pwIntentRes = CompanionBotEngine.GenerateResponse("поменяй пароль");
    Check(pwIntentRes.IsPromptingPassword, "password change intent sets IsPromptingPassword");
    Check(pwIntentRes.ReplyText.Contains("Требования к паролю"), "password change intent returns prompt text");

    var pwSuggestRes = CompanionBotEngine.GenerateResponse("придумай пароль");
    Check(!pwSuggestRes.IsPromptingPassword, "general password suggest intent does not lock into AD change");
    Check(pwSuggestRes.ReplyText.Contains("запоминающ"), "password suggest returns suggestions header");
    Check(pwSuggestRes.ReplyText.Contains("1. "), "password suggest includes numbered suggestions");

    // User asks for suggestion while in awaiting state
    var pwAwaitingSuggest = CompanionBotEngine.GenerateResponse("не знаю какой", isAwaitingPassword: true);
    Check(pwAwaitingSuggest.IsPromptingPassword, "suggest while awaiting keeps IsPromptingPassword");
    Check(pwAwaitingSuggest.ReplyText.Contains("запоминающиеся"), "suggest while awaiting returns suggestions");

    // User provides invalid candidate while awaiting
    var pwInvalidCandidate = CompanionBotEngine.GenerateResponse("bad", isAwaitingPassword: true);
    Check(pwInvalidCandidate.IsPromptingPassword, "invalid candidate keeps IsPromptingPassword");
    Check(pwInvalidCandidate.Emotion == CompanionEmotion.Sad, "invalid candidate triggers Sad emotion");
    Check(pwInvalidCandidate.Action == CompanionBotAction.None, "invalid candidate has None action");

    // User provides valid candidate while awaiting
    var pwValidCandidate = CompanionBotEngine.GenerateResponse("• KzBarys2026!", isAwaitingPassword: true);
    Check(!pwValidCandidate.IsPromptingPassword, "valid candidate clears IsPromptingPassword");
    Check(pwValidCandidate.Action == CompanionBotAction.ChangePassword, "valid candidate sets ChangePassword action");
    Check(pwValidCandidate.TargetPassword == "KzBarys2026!", "valid candidate extracts clean target password without bullet");

    // User cancels while awaiting
    var pwCancelRes = CompanionBotEngine.GenerateResponse("отмена", isAwaitingPassword: true);
    Check(!pwCancelRes.IsPromptingPassword, "cancel clears IsPromptingPassword");
    Check(pwCancelRes.Action == CompanionBotAction.None, "cancel has None action");
    Check(pwCancelRes.ReplyText.Contains("отменена"), "cancel returns cancel notice");

    // Password localization check
    Loc.Initialize("kk");
    var pwKkRes = CompanionBotEngine.GenerateResponse("пароль ауыстыру");
    Check(pwKkRes.IsPromptingPassword && pwKkRes.ReplyText.Contains("паролін өзгертуге"), "KK password change prompt");
    Loc.Initialize("en");
    var pwEnRes = CompanionBotEngine.GenerateResponse("change password");
    Check(pwEnRes.IsPromptingPassword && pwEnRes.ReplyText.Contains("login password"), "EN password change prompt");
    Loc.Initialize("zh");
    var pwZhRes = CompanionBotEngine.GenerateResponse("修改密码");
    Check(pwZhRes.IsPromptingPassword && pwZhRes.ReplyText.Contains("修改电脑登录密码"), "ZH password change prompt");
    Loc.Initialize("ru");

    // Section 22: Quiet Code White Guard web service tests
    string guardFile = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LitleHelperServer", "wwwroot", "guard-white.html");
    if (!File.Exists(guardFile)) guardFile = Path.Combine(Environment.CurrentDirectory, "LitleHelperServer", "wwwroot", "guard-white.html");
    Check(File.Exists(guardFile), "guard-white.html exists");
    string guardHtml = File.ReadAllText(guardFile);
    Check(guardHtml.Contains("КОД БЕЛЫЙ"), "guard-white.html contains huge CODE WHITE alert text");
    Check(guardHtml.Contains("000000") && guardHtml.Contains("ffffff"), "guard-white.html starts black and transitions to brilliant white");
    Check(guardHtml.Contains("/api/guard/white-events"), "guard-white.html listens to isolated SSE events");

    // Section 23: Portal password generation vs AD password change
    var pwPortalRes = CompanionBotEngine.GenerateResponse("придумай пароль для портала");
    Check(!pwPortalRes.IsPromptingPassword, "portal password request does not set IsPromptingPassword");
    Check(pwPortalRes.ReplyText.Contains("портал") || pwPortalRes.ReplyText.Contains("запоминающиеся"), "portal password request returns suggestions");
    Check(pwPortalRes.Emotion == CompanionEmotion.Joy, "portal password request returns Joy emotion");

    var pwComplexRes = CompanionBotEngine.GenerateResponse("мне нужен сложный пароль");
    Check(!pwComplexRes.IsPromptingPassword, "complex password request returns general suggestions");

    // Section 23: FeatureForbiddenByParent localization
    Loc.Initialize("ru");
    Check(Loc.T("FeatureForbiddenByParent").Contains("Папа не разрешил мне этого делать... 🥺"), "RU forbidden by parent phrase");
    Loc.Initialize("kk");
    Check(Loc.T("FeatureForbiddenByParent").Contains("Әкем бұған рұқсат бермеді... 🥺"), "KK forbidden by parent phrase");
    Loc.Initialize("en");
    Check(Loc.T("FeatureForbiddenByParent").Contains("Papa didn't allow me to do this... 🥺"), "EN forbidden by parent phrase");
    Loc.Initialize("zh");
    Check(Loc.T("FeatureForbiddenByParent").Contains("爸爸不许我这么做…… 🥺"), "ZH forbidden by parent phrase");
    Loc.Initialize("ru");

    // Section 23: PetWindow forbidden notice
    pet.ShowForbiddenNotice();
    Check((PetState)petStateField.GetValue(pet)! == PetState.Sad, "ShowForbiddenNotice sets PetState.Sad");

    // Section 23: Sound profile menu localization and preview
    Check(Loc.T("TraySoundProfile").Length > 0, "TraySoundProfile localized");
    Check(Loc.T("SoundProfileDefault").Contains("Обычный"), "SoundProfileDefault localized");
    Check(Loc.T("SoundProfileVoiceAdult").Contains("робота"), "SoundProfileVoiceAdult localized");
    Check(Loc.T("SoundProfileVoiceChild").Contains("Детский"), "SoundProfileVoiceChild localized");

    // Section 23: Guard white 10-minute auto reset and app.js link
    Check(guardHtml.Contains("alertTimer") && guardHtml.Contains("10:00"), "guard-white.html contains 10-minute countdown timer element");
    Check(guardHtml.Contains("updateTimerDisplay"), "guard-white.html implements countdown update logic");

    string appJsFile = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LitleHelperServer", "wwwroot", "app.js");
    if (!File.Exists(appJsFile)) appJsFile = Path.Combine(Environment.CurrentDirectory, "LitleHelperServer", "wwwroot", "app.js");
    Check(File.Exists(appJsFile), "app.js exists");
    string appJs = File.ReadAllText(appJsFile);
    Check(appJs.Contains("guard-white.html"), "app.js contains guard-white.html link");
    Check(appJs.Contains("open-guard-white-btn") && appJs.Contains("copy-guard-white-btn"), "app.js contains open and copy buttons for guard white");

    // Section 25: 180-min inactivity notice localization
    Loc.Initialize("ru");
    Check(Loc.T("NoticePapaSaidSmart") == "Папа сказал что я умный😊", "ru NoticePapaSaidSmart exact match");
    Loc.Initialize("kk");
    Check(Loc.T("NoticePapaSaidSmart").Contains("Әкем"), "kk NoticePapaSaidSmart localized");
    Loc.Initialize("en");
    Check(Loc.T("NoticePapaSaidSmart").Contains("Papa said"), "en NoticePapaSaidSmart localized");
    Loc.Initialize("zh");
    Check(Loc.T("NoticePapaSaidSmart").Contains("爸爸"), "zh NoticePapaSaidSmart localized");
    Loc.Initialize("ru");

    // Section 25: Image viewer detection logic
    Check(NativeMethods.IsImageViewer("photos", "test.png"), "photos process is image viewer");
    Check(NativeMethods.IsImageViewer("explorer", "sunset.JPG"), "sunset.JPG title is image viewer");
    Check(NativeMethods.IsImageViewer("i_view64", "photo.webp"), "irfanview is image viewer");
    Check(!NativeMethods.IsImageViewer("notepad", "notes.txt"), "notepad is not image viewer");
    Check(!NativeMethods.IsImageViewer("cmd", "Command Prompt"), "cmd is not image viewer");

    // Section 25: Sprite frames for TurnBack and Shy
    var testSprites = new Sprites();
    var frameBack = testSprites.Get(PetState.TurnBack, 0);
    Check(frameBack != null && frameBack.Width > 0, "TurnBack sprite frame loaded successfully");
    var frameShy = testSprites.Get(PetState.Shy, 0);
    Check(frameShy != null && frameShy.Width > 0, "Shy sprite frame loaded successfully");
    Check(Sprites.FrameCount(PetState.TurnBack) == 2, "TurnBack frame count is 2");
    Check(Sprites.FrameCount(PetState.Shy) == 2, "Shy frame count is 2");

    // Section 27: PetCat and PetDog sprites
    var frameCat = testSprites.Get(PetState.PetCat, 0);
    Check(frameCat != null && frameCat.Width > 0, "PetCat sprite frame loaded successfully");
    var frameDog = testSprites.Get(PetState.PetDog, 0);
    Check(frameDog != null && frameDog.Width > 0, "PetDog sprite frame loaded successfully");
    Check(Sprites.FrameCount(PetState.PetCat) == 2, "PetCat frame count is 2");
    Check(Sprites.FrameCount(PetState.PetDog) == 2, "PetDog frame count is 2");

    // Section 28: Animation protection
    pet.React(PetState.Dance, 5);
    Check(pet.IsAnimationLocked, "Pet animation is locked during temporary animation");
    typeof(PetWindow).GetMethod("FollowCursor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(pet, null);
    var petStateProp = typeof(PetWindow).GetField("state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(pet);
    Check((PetState)petStateProp! == PetState.Dance, "FollowCursor did not interrupt locked animation");

    // Section 27: Regex stripping of /pet commands
    string testPetBody1 = System.Text.RegularExpressions.Regex.Replace("/pet Привет", @"^(?:(?:/срочно|/танец|/pet|/погладить|/питомец)(?:\s+|$))+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    Check(testPetBody1 == "Привет", "Regex strips /pet");
    string testPetBody2 = System.Text.RegularExpressions.Regex.Replace("/погладить /срочно Привет", @"^(?:(?:/срочно|/танец|/pet|/погладить|/питомец)(?:\s+|$))+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    Check(testPetBody2 == "Привет", "Regex strips /погладить /срочно");

    var botPet = CompanionBotEngine.GenerateResponse("/pet");
    Check(botPet.RobotState is PetState.PetCat or PetState.PetDog, "CompanionBotEngine returns PetCat or PetDog for /pet");

    // 50/50 distribution check
    int catCount = 0, dogCount = 0;
    for (int i = 0; i < 100; i++)
    {
        var res = CompanionBotEngine.GenerateResponse("/pet");
        if (res.RobotState == PetState.PetCat) catCount++;
        else if (res.RobotState == PetState.PetDog) dogCount++;
    }
    Check(catCount > 20 && dogCount > 20, "Pet random distribution chooses both cats and dogs");

    // Section 29: Role typing localization
    Loc.Initialize("ru");
    Check(Loc.T("TypingPsychologistText") == "Психолог печатает…", "RU TypingPsychologistText");
    Check(Loc.T("TypingEmployeeText") == "Сотрудник печатает…", "RU TypingEmployeeText");
    Check(Loc.T("NoticePetCat") == "Робот гладит котика 🐱", "RU NoticePetCat");
    Check(Loc.T("NoticePetDog") == "Робот гладит собачку 🐶", "RU NoticePetDog");

    Loc.Initialize("kk");
    Check(Loc.T("TypingPsychologistText") == "Психолог жазып жатыр…", "KK TypingPsychologistText");
    Check(Loc.T("TypingEmployeeText") == "Қызметкер жазып жатыр…", "KK TypingEmployeeText");
    Loc.Initialize("en");
    Check(Loc.T("TypingPsychologistText") == "Psychologist is typing…", "EN TypingPsychologistText");
    Check(Loc.T("TypingEmployeeText") == "Employee is typing…", "EN TypingEmployeeText");
    Loc.Initialize("zh");
    Check(Loc.T("TypingPsychologistText") == "心理咨询师正在输入…", "ZH TypingPsychologistText");
    Check(Loc.T("TypingEmployeeText") == "员工正在输入…", "ZH TypingEmployeeText");
    Loc.Initialize("ru");

    // Section 30: Interruptible idle animations vs strictly protected intentional animations
    pet.React(PetState.Yawn, 5);
    Check(pet.IsInterruptibleIdleAnimation && !pet.IsAnimationLocked, "Yawn is interruptible idle animation");
    pet.Wake();
    petStateProp = typeof(PetWindow).GetField("state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(pet);
    Check((PetState)petStateProp! is PetState.Wake or PetState.Idle, "Wake interrupts Yawn animation");

    pet.React(PetState.Workout, 5);
    Check(pet.IsInterruptibleIdleAnimation && !pet.IsAnimationLocked, "Workout is interruptible idle animation");
    pet.Wake();
    petStateProp = typeof(PetWindow).GetField("state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(pet);
    Check((PetState)petStateProp! is PetState.Wake or PetState.Idle, "Wake interrupts Workout animation");

    pet.React(PetState.Flower, 5);
    Check(pet.IsInterruptibleIdleAnimation && !pet.IsAnimationLocked, "Flower is interruptible idle animation");
    pet.Wake();
    petStateProp = typeof(PetWindow).GetField("state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(pet);
    Check((PetState)petStateProp! is PetState.Wake or PetState.Idle, "Wake interrupts Flower animation");

    // Strictly protected animations must not be interrupted
    PetState[] protectedStates = [PetState.Dance, PetState.PetCat, PetState.PetDog, PetState.TurnBack, PetState.Shy, PetState.Celebrate, PetState.Offended, PetState.Twirl, PetState.Cry];
    foreach (var ps in protectedStates)
    {
        pet.React(ps, 5);
        Check(!pet.IsInterruptibleIdleAnimation && pet.IsAnimationLocked, $"{ps} is strictly locked and not interruptible");
        pet.Wake();
        petStateProp = typeof(PetWindow).GetField("state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(pet);
        Check((PetState)petStateProp! == ps, $"Wake does not interrupt locked {ps} animation");
    }

    // Section 31: Assistant screen position remains fixed beside taskbar when opening menu
    typeof(PetWindow).GetMethod("HideBubbles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(pet, null);
    pet.UpdateLayout();
    double initialTaskbarAnchor = System.Windows.SystemParameters.WorkArea.Bottom - 96;
    pet.Top = initialTaskbarAnchor;
    pet.Left = 300;
    pet.UpdateLayout();
    double anchorScreenY = pet.Top;
    double anchorScreenX = pet.Left;
    typeof(PetWindow).GetMethod("ShowMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(pet, null);
    pet.UpdateLayout();
    var robotImg = ((System.Windows.Controls.Canvas)pet.Content).Children.OfType<System.Windows.Controls.Image>().First();
    double actualRobotScreenY = pet.Top + System.Windows.Controls.Canvas.GetTop(robotImg);
    double actualRobotScreenX = pet.Left + System.Windows.Controls.Canvas.GetLeft(robotImg);
    Check(Math.Abs(actualRobotScreenY - anchorScreenY) < 1, "Assistant vertical screen position unchanged when opening menu near taskbar");
    Check(Math.Abs(actualRobotScreenX - anchorScreenX) < 1, "Assistant horizontal screen position unchanged when opening menu");
    typeof(PetWindow).GetMethod("HideBubbles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(pet, null);
    pet.UpdateLayout();
    Check(Math.Abs(pet.Top - anchorScreenY) < 1, "Assistant restored to exact taskbar anchor after closing menu");

    pet.Close();
    } catch (Exception ex) { spriteError = ex; } });
    thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
    if (spriteError != null) throw spriteError;
    Console.WriteLine("All checks passed.");
}
finally { Directory.Delete(temp, true); }
