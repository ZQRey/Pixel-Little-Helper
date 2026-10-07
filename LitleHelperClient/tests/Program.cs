using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PixelHelper;

static void Check(bool result, string message) { if (!result) throw new Exception(message); Console.WriteLine("PASS " + message); }

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
    pet.SetDisplayMode("Topmost"); Check(pet.Topmost, "topmost display mode enabled");
    pet.SetDisplayMode("Normal"); Check(!pet.Topmost, "normal display mode clears topmost");
    pet.SetDisplayMode("Background");
    pet.SetAssistantHidden(true); Check(!pet.IsVisible, "helper hides without terminating window"); pet.SetAssistantHidden(false); Check(pet.IsVisible, "helper can be restored from tray action");
    var trayHandle = new System.Windows.Interop.WindowInteropHelper(pet).Handle;
    using (var nativeTray = new TrayIcon(trayHandle, new Sprites().Get(PetState.Idle, 0), () => { }, () => { }, _ => { }))
    { Check(nativeTray.Registered, "native tray icon registered with Explorer"); nativeTray.SetUnread(true); nativeTray.SetUnread(false); }
    var petFlags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    var petSettings = (Settings)typeof(PetWindow).GetField("settings", petFlags)!.GetValue(pet)!; petSettings.EmojiReactions = true; petSettings.ChatDoNotDisturb = false;
    var petStateField = typeof(PetWindow).GetField("state", petFlags)!; var advanceEmoji = typeof(PetWindow).GetMethod("AdvanceEmoji", petFlags)!;
    pet.InsertEmojiReaction(HelperEmojis.All[1]); Check((PetState)petStateField.GetValue(pet)! == PetState.Joy, "inserting emoji starts matching helper animation");
    pet.React(PetState.Error, 4); pet.InsertEmojiReaction(HelperEmojis.All[5]); Check((PetState)petStateField.GetValue(pet)! == PetState.Error, "important error animation takes priority over emoji");
    typeof(PetWindow).GetField("actionUntil", petFlags)!.SetValue(pet, DateTime.UtcNow.AddSeconds(-1)); advanceEmoji.Invoke(pet, null); Check((PetState)petStateField.GetValue(pet)! == PetState.Laugh, "deferred emoji plays when important animation ends");
    typeof(PetWindow).GetField("emojiUntil", petFlags)!.SetValue(pet, DateTime.UtcNow.AddSeconds(-1)); advanceEmoji.Invoke(pet, null); Check((PetState)petStateField.GetValue(pet)! == PetState.Idle, "helper returns to idle after emoji reaction");
    typeof(PetWindow).GetMethod("ChangeState", petFlags)!.Invoke(pet, new object[] { PetState.Sleep }); pet.InsertEmojiReaction(HelperEmojis.All[0]);
    typeof(PetWindow).GetField("emojiUntil", petFlags)!.SetValue(pet, DateTime.UtcNow.AddSeconds(-1)); advanceEmoji.Invoke(pet, null); Check((PetState)petStateField.GetValue(pet)! == PetState.Sleep, "sleeping helper returns to sleep after emoji");
    petSettings.ChatDoNotDisturb = true; pet.InsertEmojiReaction(HelperEmojis.All[1]); Check((PetState)petStateField.GetValue(pet)! == PetState.Sleep, "do not disturb suppresses emoji animation"); petSettings.ChatDoNotDisturb = false;
    typeof(PetWindow).GetMethod("ChangeState", petFlags)!.Invoke(pet, new object[] { PetState.Idle });
    Check(pet.InputHitTest(new System.Windows.Point(48, 32)) is System.Windows.Controls.Image, "robot receives WPF input after canvas translation");
    typeof(PetWindow).GetMethod("ShowMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(pet, null);
    pet.UpdateLayout();
    Check(pet.ActualWidth == 500 && pet.ActualHeight == 400, "expanded menu layout");
    Check(pet.InputHitTest(new System.Windows.Point(250, 318)) is System.Windows.Controls.Image, "robot input remains correct with menu open");
    var menuCanvas = (System.Windows.Controls.Canvas)pet.Content;
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
    pet.ReceiveAnnouncement(new("Первое", "Администратор", 30));
    for(int n=0;n<10;n++)Check(pet.ReceiveAnnouncement(new("Очередь "+n,"Администратор",30)).Contains("очередь"),"queued notice "+n);
    bool full=false;try{pet.ReceiveAnnouncement(new("Лишнее","Администратор",30));}catch(InvalidOperationException){full=true;}Check(full,"notice queue bounded to ten pending messages");
    var ticketWindow = new TicketWindow((title, description, branch, room, cancellation) => Task.FromResult(1), cancellation => Task.FromResult(new List<TicketBranch> { new(1, "Поликлиника"), new(2, "Больница") }), new Settings { TicketBranchId = 1, TicketRoom = "12" }, CancellationToken.None);
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
    pet.Close();
    } catch (Exception ex) { spriteError = ex; } });
    thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
    if (spriteError != null) throw spriteError;
    Console.WriteLine("All checks passed.");
}
finally { Directory.Delete(temp, true); }
