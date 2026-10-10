using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PixelHelper;

public sealed class PetWindow : Window
{
    private const double SpriteLeft = 202, SpriteTopNormal = 286, SpriteTopMenu = 152, Scale = 2;
    private double currentSpriteTop = SpriteTopNormal;
    private double SpriteTop => currentSpriteTop;

    private void SetSpriteTop(double top)
    {
        if (Math.Abs(currentSpriteTop - top) < 0.001) return;
        currentSpriteTop = top;
        Canvas.SetTop(robot, top);
        ClearRegions();
    }
    private readonly Canvas canvas = new();
    private readonly Image robot = new() { Width = 96, Height = 96, Cursor = Cursors.Hand };
    private readonly List<FrameworkElement> bubbles = [];
    private readonly Settings settings = Settings.Load();
    private readonly RobotClicks robotClicks = new();
    private PetState? localNoticeAnimation;
    private HubConnectionService? hub;
    private readonly Sprites sprites = new();
    private readonly FaceBadges faceBadges = new();
    private readonly Image faceOverlay = new() { Width = 30, Height = 22, IsHitTestVisible = false };
    private readonly Image hatOverlay = new() { Width = 32, Height = 32, IsHitTestVisible = false };
    private BitmapSource? birthdayHatSource;
    private readonly List<DateTime> rapidClicks = new();
    private DateTime lastSixtyMinReaction = DateTime.MinValue;
    private CompanionChatWindow? companionWindow;
    private DiskSpaceMonitor? diskMonitor;
    private FaceNoticeStatus faceStatus = FaceNoticeStatus.None;
    private DateTime faceStatusUntil;
    private int faceHeartFrame;
    private int unreadChatsCount;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer animation = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer inactivity = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer refresh = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(30) };
    private readonly DispatcherTimer outsideClick = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer unreadReminderTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(5) };
    private readonly Dictionary<(PetState, int, double, double), nint> regionCache = new();
    private HwndSource? source;
    private nint handle;
    private SpriteFrame current = null!;
    private PetState state;
    private int frame;
    private DateTime lastInteraction = DateTime.UtcNow;
    private DateTime actionUntil;
    private List<AssistantAction> actions;
    private bool menuOpen, mouseDown, dragging, wokeOnDown, previousLeft;
    private NativeMethods.POINT dragStart;
    private double startLeft, startTop;
    private bool refreshing;
    private TicketWindow? ticket;
    private SuperAdminWindow? superWindow;
    private EmergencyAlertWindow? activeAlertWindow;
    private bool superAvailable, announcementVisible;
    private MessengerClient? messenger;
    private MessengerWindow? messengerWindow;
    private TrayIcon? tray;
    private readonly EmojiReactions emojiReactions;
    private bool emojiAnimating;
    private PetState emojiRestore;
    private DateTime emojiUntil;
    private readonly Dictionary<int, long> chatUnread = new();
    private bool refreshingChat;
    private DateTime lastChatSound;
    private int chatUserId;
    private double rapidDistance;
    private DateTime motionStarted;
    private NativeMethods.POINT previousDragPoint;
    private bool dizzyDrag;
    private readonly Queue<ClientNotice> announcements = new();
    private readonly Queue<(string Key, DateTime Expires)> dances = new();
    private readonly Queue<(string Key, DateTime Expires)> pets = new();
    private bool urgentVisible;
    private readonly HashSet<string> urgentShown = new();
    private DateTime announcementUntil;
    private DateTime lastOneEightyMinNotice;
    private nint lastViewerHwnd;
    private DateTime imageReactionPhase1Until;
    private DateTime imageReactionPhase2Until;
    private bool imageReactionActive;
    private readonly bool diagnostics;
    private bool isOneEightyMinNoticeActive;
    private bool expanded = true;
    internal int AnimatedFrames { get; private set; }
    private double RobotX => expanded ? Canvas.GetLeft(robot) : 0;
    private double RobotY => expanded ? Canvas.GetTop(robot) : 0;

    public PetWindow(bool diagnostics = false)
    {
        this.diagnostics = diagnostics;
        Loc.Initialize(settings.Language);
        emojiReactions = new EmojiReactions(diagnostics ? null : Path.Combine(Settings.Folder, "emoji-seen.json"));
        Width = 500; Height = 400;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = null;
        ShowInTaskbar = false;
        ShowActivated = false;
        if (settings.DisplayMode is not ("Topmost" or "Normal" or "Background")) settings.DisplayMode = "Topmost";
        if (diagnostics) settings.DisplayMode = "Background";
        else { settings.DisplayMode = "Topmost"; settings.AssistantHidden = false; }
        Topmost = settings.DisplayMode == "Topmost";
        Title = "PixelHelper";
        Content = canvas;
        actions = ApiClient.Defaults();
        unreadReminderTimer.Tick += (_, _) => CheckUnreadReminder();
        if (!diagnostics)
        {
            try
            {
                messenger = new MessengerClient(settings);
                messenger.Changed += () => Dispatcher.BeginInvoke(new Action(async () => await CheckChatAsync()));
                messenger.MessageReceived += message => Dispatcher.BeginInvoke(new Action(async () => { ReceiveEmoji(message); await CheckChatAsync(); }));
                messenger.ReminderSettingsUpdated += minutes => Dispatcher.BeginInvoke(new Action(() => unreadReminderTimer.Interval = TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 1440))));
            }
            catch (Exception ex) { Settings.Log(ex); }
            InitializeHub();
        }
        RenderOptions.SetBitmapScalingMode(robot, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(faceOverlay, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(hatOverlay, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(robot, SpriteLeft); Canvas.SetTop(robot, SpriteTop);
        canvas.Children.Add(robot);
        canvas.Children.Add(faceOverlay);
        canvas.Children.Add(hatOverlay);
        LoadBirthdayHat();
        if (!diagnostics)
        {
            diskMonitor = new DiskSpaceMonitor(freePercent =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    React(PetState.Notice, 5);
                    ReceiveAnnouncement(new ClientNotice(Loc.T("DiskSpaceLowWarning"), "PixelHelper", 30) { IsDiskAlert = true });
                });
            });
            diskMonitor.Start();
            Task.Run(() => CompanionStorage.AutoPurge());
        }
        robot.MouseEnter += (_, _) => Wake();
        robot.MouseLeftButtonDown += MouseDownRobot;
        robot.MouseMove += MouseMoveRobot;
        robot.MouseLeftButtonUp += MouseUpRobot;
        robot.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (mouseDown) return;
            Wake(); HideBubbles();
            var menu = new ContextMenu { PlacementTarget = robot, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
            if (hub?.IsOnline == true && superAvailable)
            {
                var admin = new MenuItem { Header = "Кнопки супер админа" };
                admin.Click += (_, _) => OpenSuperAdminWindow();
                menu.Items.Add(admin);
            }
            var quiet = new MenuItem { Header = "Не беспокоить", IsCheckable = true, IsChecked = settings.ChatDoNotDisturb };
            quiet.Click += (_, _) => { settings.ChatDoNotDisturb = quiet.IsChecked; settings.Save(); };
            menu.Items.Add(quiet);
            AddSoundProfileMenu(menu);
            var exit = new MenuItem { Header = "Выход" };
            exit.Click += (_, _) => Close();
            menu.Items.Add(exit);
            menu.IsOpen = true;
        };
        robot.LostMouseCapture += (_, _) => { if (mouseDown) EndDrag(); };
        SourceInitialized += InitializeNative;
        Loaded += async (_, _) =>
        {
            
            animation.Start(); inactivity.Start(); refresh.Start();
            if (!diagnostics) unreadReminderTimer.Start();
            if (!diagnostics) hub?.Start();
            if (!diagnostics && messenger != null) _ = messenger.StartAsync();
            if (!diagnostics && hub != null)
            {
                var ncaWatchdog = new NcaLayerWatchdog(hub, msg => Dispatcher.InvokeAsync(() => ShowNotice(msg)));
                ncaWatchdog.StartDelayed();
                var sessionWatchdog = new SessionWatchdog(ncaWatchdog, msg => Dispatcher.InvokeAsync(() => ShowNotice(msg)));
                sessionWatchdog.Start();
            }
            if (!diagnostics) React(PetState.Greeting, 3);
            if (!diagnostics) { Settings.PrepareStartup(); var greeting = await Task.Run(UserGreeting.Text); if (!Dispatcher.HasShutdownStarted) { ReceiveAnnouncement(new ClientNotice(greeting, "PixelHelper", 10)); React(PetState.Greeting, 3); } }
            if (!diagnostics && string.IsNullOrWhiteSpace(settings.TicketRoom))
            {
                _ = Dispatcher.BeginInvoke(new Action(PromptForRoom), System.Windows.Threading.DispatcherPriority.Background);
            }
        };
        LocationChanged += (_, _) => ApplyDisplayMode();
        animation.Tick += (_, _) =>
        {
            AdvanceEmoji();
            if (!emojiAnimating && IsTemporary(state) && DateTime.UtcNow >= actionUntil) ChangeState(state == PetState.Yawn ? PetState.Sleep : PetState.Idle);
            FollowCursor();
            frame++; AnimatedFrames++; Draw();
        };
        inactivity.Tick += (_, _) =>
        {
            AdvanceAnnouncements();
            if (!menuOpen && !mouseDown && !IsAnimationLocked && dances.Count > 0 && ChatDesktop.Unlocked()) { var dance = dances.Dequeue(); if (dance.Expires > DateTime.UtcNow) { settings.ChatCommandsShown.Add(dance.Key); settings.ChatCommandsShown = settings.ChatCommandsShown.TakeLast(1000).ToList(); if (!diagnostics) settings.Save(); TriggerDance(6); } }
            if (!menuOpen && !mouseDown && !IsAnimationLocked && pets.Count > 0 && ChatDesktop.Unlocked()) { var pet = pets.Dequeue(); if (pet.Expires > DateTime.UtcNow) { settings.ChatCommandsShown.Add(pet.Key); settings.ChatCommandsShown = settings.ChatCommandsShown.TakeLast(1000).ToList(); if (!diagnostics) settings.Save(); TriggerPet(6); } }
            AdvanceEmoji();
            ApplyDisplayMode();
            bool exposed = IsExposed();
            if (exposed && !animation.IsEnabled) animation.Start();
            else if (!exposed && animation.IsEnabled) animation.Stop();
            if (!IsAnimationLocked && !emojiAnimating && !menuOpen && !mouseDown && ticket == null && state != PetState.Charging &&
                (DateTime.UtcNow - lastInteraction >= TimeSpan.FromMinutes(180) || NativeMethods.IdleTime() >= TimeSpan.FromMinutes(180)) &&
                DateTime.UtcNow - lastOneEightyMinNotice >= TimeSpan.FromMinutes(30))
            {
                lastOneEightyMinNotice = DateTime.UtcNow;
                isOneEightyMinNoticeActive = true;
                React(PetState.Joy, 5);
                ShowNotice(Loc.T("NoticePapaSaidSmart"));
            }
            else if (!IsAnimationLocked && !emojiAnimating && !menuOpen && !mouseDown && ticket == null && state != PetState.Charging &&
                (DateTime.UtcNow - lastInteraction >= TimeSpan.FromMinutes(60) || NativeMethods.IdleTime() >= TimeSpan.FromMinutes(60)) &&
                DateTime.UtcNow - lastSixtyMinReaction >= TimeSpan.FromMinutes(15))
            {
                lastSixtyMinReaction = DateTime.UtcNow;
                if (string.Equals(settings.LicenseStatus, "Suspended", StringComparison.OrdinalIgnoreCase))
                {
                    React(PetState.Notice, 5);
                    ShowNotice(Loc.T("NoticeTemporaryHelper"));
                }
                else
                {
                    React(PetState.Flower, 5);
                }
            }
            else if (!IsAnimationLocked && !emojiAnimating && !menuOpen && !mouseDown && ticket == null && state != PetState.Charging &&
                (DateTime.UtcNow - lastInteraction >= TimeSpan.FromMinutes(30) || NativeMethods.IdleTime() >= TimeSpan.FromMinutes(30)))
            {
                if (state != PetState.Workout) ChangeState(PetState.Workout);
            }
            else if (!IsAnimationLocked && !emojiAnimating && !menuOpen && !mouseDown && ticket == null && state is not (PetState.Sleep or PetState.Yawn or PetState.Workout or PetState.Charging) &&
                (DateTime.UtcNow - lastInteraction > TimeSpan.FromMinutes(5) || NativeMethods.IdleTime() > TimeSpan.FromMinutes(5)))
            {
                React(PetState.Yawn, 2);
            }
            CheckImageViewer();
        };
        refresh.Tick += async (_, _) => await RefreshActions();
        outsideClick.Tick += (_, _) =>
        {
            bool down = NativeMethods.GetAsyncKeyState(1) < 0 || NativeMethods.GetAsyncKeyState(2) < 0;
            if (!announcementVisible && down && !previousLeft && NativeMethods.GetCursorPos(out var p) && NativeMethods.WindowFromPoint(p) != handle)
                HideBubbles();
            previousLeft = down;
        };
        Closed += async (_, _) =>
        {
            animation.Stop(); inactivity.Stop(); refresh.Stop(); outsideClick.Stop();
            lifetime.Cancel(); ticket?.Close(); superWindow?.Close(); source?.RemoveHook(Hook);
            messengerWindow?.Close();
            tray?.Dispose();
            if (messenger != null) await messenger.DisposeAsync();
            ClearRegions(); SavePosition();
            if (hub != null) await hub.DisposeAsync();
            lifetime.Dispose();
        };
        var work = SystemParameters.WorkArea;
        Left = settings.X is double x && double.IsFinite(x) ? x : work.Left + (work.Width - Width) / 2;
        Top = settings.Y is double y && double.IsFinite(y) ? y : work.Bottom - SpriteTop - robot.Height - 10;
        ClampPosition();
        Draw();
        Collapse();
    }
    private void InitializeNative(object? sender, EventArgs e)
    {
        handle = new WindowInteropHelper(this).Handle;
        source = HwndSource.FromHwnd(handle);
        source.AddHook(Hook);
        NativeMethods.ToolWindow(handle, true);
        ApplyDisplayMode();
        if (!diagnostics) tray = new TrayIcon(handle, new SpriteFrame("app-icon"), () => _ = OpenMessengerAsync(), ShowTrayMenu, peer => _ = OpenMessengerAsync(peer == 0 ? null : peer));
        UpdateRegion();
        if (!diagnostics) hub?.Start();
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.WM_WINDOWPOSCHANGING && settings.DisplayMode == "Background" && !urgentVisible)
        {
            var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
            if ((pos.Flags & NativeMethods.SWP_NOZORDER) == 0)
            {
                pos.HwndInsertAfter = NativeMethods.DesktopAnchor(hwnd);
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        if (message == NativeMethods.WM_DPICHANGED)
            Dispatcher.BeginInvoke(new Action(() => { ClearRegions(); UpdateRegion(); ClampPosition(); }), DispatcherPriority.Loaded);
        if (message == NativeMethods.WM_NCHITTEST)
        {
            long packed = lParam.ToInt64();
            var point = PointFromScreen(new Point(unchecked((short)(packed & 0xFFFF)), unchecked((short)((packed >> 16) & 0xFFFF))));
            bool hit = current.Opaque((int)Math.Floor((point.X - RobotX) / Scale), (int)Math.Floor((point.Y - RobotY) / Scale)) ||
                bubbles.Any(b => new Rect(Canvas.GetLeft(b), Canvas.GetTop(b), b.ActualWidth, b.ActualHeight).Contains(point));
            handled = true;
            return new nint(hit ? 1 : -1);
        }
        return 0;
    }
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try { source?.RemoveHook(Hook); } catch { }
        ClearRegions();
    }
    private void ApplyDisplayMode()
    {
        if (handle == 0) return;
        if (urgentVisible) { Topmost = true; return; }
        if (settings.DisplayMode == "Background") NativeMethods.Bottom(handle);
    }
    internal void SetDisplayMode(string mode)
    {
        if (mode is not ("Topmost" or "Normal" or "Background")) throw new ArgumentException("Неизвестный режим.");
        settings.DisplayMode = mode; Topmost = urgentVisible || mode == "Topmost";
        NativeMethods.SetWindowPos(handle, Topmost ? new nint(-1) : new nint(-2), 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        ApplyDisplayMode(); if (!diagnostics) settings.Save();
    }
    internal void SetAssistantHidden(bool hidden)
    {
        settings.AssistantHidden = hidden; if (hidden && !urgentVisible) { HideBubbles(); Hide(); animation.Stop(); } else { Show(); ApplyDisplayMode(); }
        if (!diagnostics) settings.Save();
    }
    internal void RestoreDisplayPreferences()
    {
        if (diagnostics) return;
        settings.DisplayMode = "Topmost";
        settings.AssistantHidden = false;
        SetDisplayMode("Topmost");
    }
    private void PromptForRoom()
    {
        if (diagnostics || hub == null) return;
        try
        {
            var dlg = new RoomPromptWindow(settings, hub) { Owner = this };
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }
    private void HandleRoomUpdated(string room)
    {
        if (string.IsNullOrWhiteSpace(room)) return;
        string trimmed = room.Trim();
        if (settings.TicketRoom != trimmed)
        {
            settings.TicketRoom = trimmed;
            if (!diagnostics) settings.Save();
            ReceiveAnnouncement(new ClientNotice($"Кабинет обновлен: {trimmed}", "PixelHelper", 8) { LocalAnimation = PetState.Joy });
        }
    }
    private void HandleLiquidationProtocol()
    {
        try
        {
            ChangeState(PetState.Cry);
            var farewell = new FarewellLiquidationWindow();
            farewell.Show();
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }
    private void AddDisplayMenu(ContextMenu menu)
    {
        var quiet = new MenuItem { Header = "Не беспокоить", IsCheckable = true, IsChecked = settings.ChatDoNotDisturb };
        quiet.Click += (_, _) => { settings.ChatDoNotDisturb = quiet.IsChecked; settings.Save(); };
        menu.Items.Add(quiet);
    }
    private void InitializeHub()
    {
            try
            {
                hub = new HubConnectionService(settings);
                hub.SuperAdminAvailable += available => Dispatcher.BeginInvoke(new Action(() => { superAvailable = available; if (menuOpen && !announcementVisible) ShowMenu(); }));
                hub.NoticeReceived += notice => Dispatcher.InvokeAsync(() => ReceiveAnnouncement(notice)).Task;
                hub.CartridgeReadyReceived += notice => Dispatcher.InvokeAsync(() => HandleCartridgeReady(notice));
                hub.TicketReplyReceived += notice => Dispatcher.InvokeAsync(() => HandleTicketReply(notice));
                hub.EmergencyAlertReceived += notice => Dispatcher.InvokeAsync(() => HandleEmergencyAlert(notice));
                hub.EmergencyAlertCanceled += () => Dispatcher.InvokeAsync(HandleEmergencyAlertCanceled);
                hub.RoomUpdated += room => Dispatcher.InvokeAsync(() => HandleRoomUpdated(room));
                hub.LiquidationReceived += () => Dispatcher.InvokeAsync(HandleLiquidationProtocol);
                hub.Connecting += () => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!mouseDown && !menuOpen && !IsTemporary(state) && state != PetState.Drag) ChangeState(PetState.Charging);
                }));
                hub.OnlineChanged += online => Dispatcher.BeginInvoke(new Action(() =>
                {
                    ShowConnectionResult(online);
                    if (!online) { actions = ApiClient.Defaults(); if (menuOpen && !announcementVisible) ShowMenu(); }
                }));
                hub.ButtonsUpdated += buttons => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (hub.IsOnline)
                    {
                        actions = buttons.Where(b => b.IsActive).OrderBy(b => b.OrderIndex).ThenBy(b => b.Id).Take(12)
                            .Select(b => new AssistantAction(b.Id.ToString(), b.GetLocalizedTitle(Loc.Code), b.ActionType switch { "open_folder" => "open_path", "ticket" => "it_ticket", _ => b.ActionType }, b.Payload)).ToList();
                        if (menuOpen && !announcementVisible) ShowMenu();
                    }
                }));
            }
            catch (Exception ex) { Settings.Log(ex); }
    }
    private bool reconnecting;
    internal async Task ReconnectAsync()
    {
        if (reconnecting || diagnostics) return;
        reconnecting = true;
        try
        {
            if (hub != null) await hub.DisposeAsync();
            hub = null; InitializeHub(); hub?.Start();
            if (messenger != null) await messenger.SignInWindowsAsync(true);
        }
        catch (Exception ex) { Settings.Log(ex); React(PetState.Error, 4); }
        finally { reconnecting = false; }
    }
    private void ShowTrayMenu()
    {
        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        menu.Items.Add(new MenuItem { Header = "Помощник: " + (hub?.IsOnline == true ? "Online" : "Offline") + " · " + (hub?.Status ?? "Не запущен"), IsEnabled = false });
        menu.Items.Add(new MenuItem { Header = "Мессенджер: " + (messenger?.ConnectionStatus ?? "Не запущен"), IsEnabled = false });
        if (hub?.LastSuccess is DateTime success) menu.Items.Add(new MenuItem { Header = "Последняя связь: " + success.ToLocalTime().ToString("HH:mm:ss"), IsEnabled = false });
        var chat = new MenuItem { Header = "Мессенджер" }; chat.Click += async (_, _) => await OpenMessengerAsync(); menu.Items.Add(chat);
        if (hub?.IsOnline == true && superAvailable) { var admin = new MenuItem { Header = "Кнопки супер админа" }; admin.Click += (_, _) => OpenSuperAdminWindow(); menu.Items.Add(admin); }
        var reconnect = new MenuItem { Header = "Повторить подключение", IsEnabled = !reconnecting }; reconnect.Click += async (_, _) => await ReconnectAsync(); menu.Items.Add(reconnect);
        AddDisplayMenu(menu);
        AddSoundProfileMenu(menu);
        var langMenu = new MenuItem { Header = Loc.T("TrayLanguage") };
        foreach (var lang in Enum.GetValues<AppLanguage>())
        {
            var item = new MenuItem { Header = Loc.DisplayName(lang), IsCheckable = true, IsChecked = Loc.CurrentLanguage == lang };
            item.Click += (_, _) => { Loc.CurrentLanguage = lang; settings.Language = Loc.Code; settings.Save(); React(PetState.Twirl, 1.2); };
            langMenu.Items.Add(item);
        }
        menu.Items.Add(langMenu);
        menu.Items.Add(new Separator()); var exit = new MenuItem { Header = Loc.T("TrayExit") }; exit.Click += (_, _) => Close(); menu.Items.Add(exit); menu.IsOpen = true;
    }
    private void AddSoundProfileMenu(ContextMenu menu)
    {
        var soundMenu = new MenuItem { Header = Loc.T("TraySoundProfile") };
        string[] profiles = ["Sound", "VoiceAdult", "VoiceChild"];
        foreach (var prof in profiles)
        {
            string labelKey = prof switch
            {
                "VoiceAdult" => "SoundProfileVoiceAdult",
                "VoiceChild" => "SoundProfileVoiceChild",
                _ => "SoundProfileDefault"
            };
            var item = new MenuItem
            {
                Header = Loc.T(labelKey),
                IsCheckable = true,
                IsChecked = string.Equals(settings.SoundProfile, prof, StringComparison.OrdinalIgnoreCase)
            };
            item.Click += (_, _) =>
            {
                settings.SoundProfile = prof;
                settings.Save();
                SoundManager.PlayPreview(prof, SoundEvent.MessageReceived);
                React(PetState.Twirl, 1.5);
            };
            soundMenu.Items.Add(item);
        }
        menu.Items.Add(soundMenu);
    }
    private static int GetHeadBob(PetState s, int f) => s switch
    {
        PetState.Idle => (f % 4) switch { 1 => 1, 3 => -1, _ => 0 },
        PetState.Charging => (f % 4) switch { 1 or 3 => 1, _ => 0 },
        PetState.Workout => (f % 8) switch { 1 or 3 => -1, 4 or 6 => 1, 5 => 2, 7 => -1, _ => 0 },
        PetState.Facepalm => (f % 2) == 1 ? 1 : 0,
        PetState.Joy or PetState.Laugh or PetState.Celebrate => -(f % 2) * 2,
        _ => 0
    };
    private void UpdateFaceOverlay()
    {
        if (DateTime.UtcNow >= faceStatusUntil && faceStatus != FaceNoticeStatus.None)
        {
            faceStatus = FaceNoticeStatus.None;
        }

        if (faceStatus == FaceNoticeStatus.Heart)
        {
            faceHeartFrame++;
        }

        var badge = faceBadges.Get(faceStatus, faceStatus == FaceNoticeStatus.None ? unreadChatsCount : 0, faceHeartFrame);
        if (badge != null)
        {
            int bob = GetHeadBob(state, frame);
            Canvas.SetLeft(faceOverlay, RobotX + 16 * Scale);
            Canvas.SetTop(faceOverlay, RobotY + (12 + bob) * Scale);
            faceOverlay.Source = badge;
            faceOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            faceOverlay.Visibility = Visibility.Collapsed;
        }
    }
    internal void ShowFaceHeart(double seconds = 4)
    {
        faceStatus = FaceNoticeStatus.Heart;
        faceStatusUntil = DateTime.UtcNow.AddSeconds(seconds);
        faceHeartFrame = 0;
        Wake();
        SoundManager.Play(SoundEvent.MessageReceived, settings);
        Draw();
    }
    internal void ShowFaceMail(double seconds = 4)
    {
        faceStatus = FaceNoticeStatus.Mail;
        faceStatusUntil = DateTime.UtcNow.AddSeconds(seconds);
        Wake();
        Draw();
    }
    internal void ShowConnectionResult(bool connected)
    {
        faceStatus = connected ? FaceNoticeStatus.Connected : FaceNoticeStatus.Disconnected;
        faceStatusUntil = DateTime.UtcNow.AddSeconds(4);
        if (state == PetState.Charging) ChangeState(PetState.Idle);
        if (connected)
        {
            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
        }
        Draw();
    }
    private void Draw()
    {
        current = sprites.Get(state, frame);
        robot.Source = current.Image;
        UpdateFaceOverlay();
        UpdateBirthdayHat();
        UpdateRegion();
    }
    private void UpdateRegion()
    {
        if (source?.CompositionTarget == null) return;
        var m = source.CompositionTarget.TransformToDevice;
        var key = (state, frame % Sprites.FrameCount(state), m.M11, m.M22);
        if (!regionCache.TryGetValue(key, out var region))
        {
            region = NativeMethods.BuildRegion(current.Runs(RobotX, RobotY, Scale).Concat(
                bubbles.Select(b => new Rect(Canvas.GetLeft(b), Canvas.GetTop(b), b.Width, b.Height))), m.M11, m.M22);
            regionCache[key] = region;
        }
        NativeMethods.ApplyRegion(handle, region);
    }
    private void ClearRegions()
    {
        foreach (var region in regionCache.Values) NativeMethods.FreeRegion(region);
        regionCache.Clear();
    }
    public bool IsInterruptibleIdleAnimation => state is PetState.Yawn or PetState.Workout or PetState.Flower || (isOneEightyMinNoticeActive && state == PetState.Joy);
    public bool IsAnimationLocked => IsTemporary(state) && !IsInterruptibleIdleAnimation && DateTime.UtcNow < actionUntil;
    private void ChangeState(PetState value)
    {
        if (state == value) return;
        state = value; frame = 0;
        animation.Interval = TimeSpan.FromMilliseconds(value == PetState.Twirl ? 150 : value == PetState.Offended ? 450 : value == PetState.Sleep ? 250 : 100);
        Draw();
    }
    private static bool IsTemporary(PetState value) => value is PetState.Action or PetState.Greeting or PetState.Success or PetState.Error or PetState.Notice or PetState.Yawn or PetState.Wake or PetState.Dizzy or PetState.Joy or PetState.Sad or PetState.Surprise or PetState.Laugh or PetState.Think or PetState.Celebrate or PetState.Offended or PetState.Twirl or PetState.Dance or PetState.Facepalm or PetState.Flower or PetState.Cry or PetState.TurnBack or PetState.Shy or PetState.PetCat or PetState.PetDog;
    internal void React(PetState value, double seconds)
    { emojiAnimating = false; actionUntil = DateTime.UtcNow.AddSeconds(seconds); ChangeState(value); }
    private bool EmojiAllowed => settings.EmojiReactions && !settings.ChatDoNotDisturb && !settings.AssistantHidden && IsVisible && (diagnostics || ChatDesktop.Unlocked());
    internal void InsertEmojiReaction(HelperEmoji emoji)
    { if (EmojiAllowed) { emojiReactions.Insert(emoji); AdvanceEmoji(); } }
    private bool CheckSingleEmojiReaction(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!EmojiAllowed) return false;

        string trimmed = text.Trim();

        // 1. Single Heart ❤️ -> Pulsing heart on robot face for 4 seconds
        if (trimmed is "❤️" or "\u2764" or "\u2764\uFE0F" or "💖" or "💓")
        {
            ShowFaceHeart(4);
            return true;
        }

        // 2. Single Laugh 😁 -> Laughing animation for 4 seconds
        if (trimmed is "😁" or "\U0001F601" or "\uD83D\uDE01")
        {
            Wake();
            React(PetState.Laugh, 4);
            SoundManager.Play(SoundEvent.MessageReceived, settings);
            return true;
        }

        // 3. Single Facepalm 🤦‍♂️ -> Facepalm animation for 5 seconds
        if (trimmed is "🤦‍♂️" or "🤦" or "🤦‍♀️" or "\U0001F926\u200D\u2642\uFE0F" or "\U0001F926" or "\U0001F926\u200D\u2640\uFE0F")
        {
            Wake();
            React(PetState.Facepalm, 5);
            return true;
        }

        return false;
    }
    private void ReceiveEmoji(ChatEntry message)
    {
        if (messenger?.SignedIn != true || message.SenderId == messenger.UserId) return;
        if (string.IsNullOrWhiteSpace(message.Body)) return;

        if (CheckSingleEmojiReaction(message.Body.Trim())) return;

        emojiReactions.Incoming(messenger.IdentityContext, message, EmojiAllowed && settings.IncomingEmojiReactions); AdvanceEmoji();
    }
    private void AdvanceEmoji()
    {
        var now = DateTime.UtcNow;
        if (!EmojiAllowed) { emojiReactions.Clear(); if (emojiAnimating) { emojiAnimating = false; ChangeState(emojiRestore); } return; }
        if (mouseDown || state == PetState.Busy || state == PetState.Drag) { emojiAnimating = false; return; }
        if (emojiAnimating) { if (now < emojiUntil) return; emojiAnimating = false; ChangeState(emojiRestore); }
        if (IsAnimationLocked) return;
        var emoji = emojiReactions.Take(now); if (emoji == null) return;
        emojiRestore = state == PetState.Sleep ? PetState.Sleep : PetState.Idle;
        emojiAnimating = true; emojiUntil = now.AddSeconds(emoji.Seconds); ChangeState(emoji.State);
        if (!animation.IsEnabled && IsExposed()) animation.Start();
    }
    internal void Wake()
    {
        lastInteraction = DateTime.UtcNow;
        if (state is PetState.Sleep or PetState.Yawn or PetState.Workout or PetState.Flower || (isOneEightyMinNoticeActive && state == PetState.Joy))
        {
            if (isOneEightyMinNoticeActive) { isOneEightyMinNoticeActive = false; HideBubbles(); }
            actionUntil = DateTime.MinValue;
            React(PetState.Wake, 2);
        }
    }
    private void FollowCursor()
    {
        if (IsInterruptibleIdleAnimation && !mouseDown && IsVisible)
        {
            if (NativeMethods.GetCursorPos(out var curPos))
            {
                var pt = PointFromScreen(new Point(curPos.X, curPos.Y));
                double distance = Math.Abs(pt.X - RobotX - 48) + Math.Abs(pt.Y - RobotY - 40);
                if (distance < 280)
                {
                    actionUntil = DateTime.MinValue;
                    if (isOneEightyMinNoticeActive) { isOneEightyMinNoticeActive = false; HideBubbles(); }
                    Wake();
                }
            }
        }
        if (IsAnimationLocked || state is not (PetState.Idle or PetState.LookLeft or PetState.LookRight or PetState.LookUp or PetState.LookDown) || mouseDown || !IsVisible || !NativeMethods.GetCursorPos(out var cursor)) return;
        var point = PointFromScreen(new Point(cursor.X, cursor.Y));
        double dx = point.X - RobotX - 48, dy = point.Y - RobotY - 40;
        ChangeState(Math.Abs(dx) + Math.Abs(dy) > 250 || Math.Abs(dx) + Math.Abs(dy) < 20 ? PetState.Idle : Math.Abs(dx) > Math.Abs(dy) ? (dx < 0 ? PetState.LookLeft : PetState.LookRight) : (dy < 0 ? PetState.LookUp : PetState.LookDown));
    }
    private bool IsExposed()
    {
        var points = new[] { new Point(RobotX + 36, RobotY + 30), new Point(RobotX + 50, RobotY + 32), new Point(RobotX + 48, RobotY + 65) }
            .Concat(bubbles.Select(b => new Point(Canvas.GetLeft(b) + b.Width / 2, Canvas.GetTop(b) + b.Height / 2)));
        foreach (var point in points)
        {
            var screen = PointToScreen(point);
            if (NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = (int)screen.X, Y = (int)screen.Y }) == handle) return true;
        }
        return false;
    }
    internal Point DiagnosticPoint(double x, double y) => PointToScreen(new Point(x - (expanded ? 0 : SpriteLeft), y - (expanded ? 0 : SpriteTop)));
    private void Expand()
    {
        SetSpriteTop(SpriteTopNormal);
        if (expanded) return;
        double anchorX = Left, anchorY = Top;
        expanded = true;
        canvas.RenderTransform = Transform.Identity;
        Canvas.SetLeft(robot, SpriteLeft);
        Canvas.SetTop(robot, SpriteTopNormal);
        Left = anchorX - SpriteLeft; Top = anchorY - SpriteTopNormal;
        Width = 500; Height = 400;
        ClearRegions(); UpdateRegion();
    }
    private void Collapse()
    {
        if (!expanded || bubbles.Count > 0) return;
        double anchorX = Left + Canvas.GetLeft(robot);
        double anchorY = Top + Canvas.GetTop(robot);
        expanded = false;
        SetSpriteTop(SpriteTopNormal);
        canvas.RenderTransform = new TranslateTransform(-SpriteLeft, -SpriteTop);
        Canvas.SetLeft(robot, SpriteLeft);
        Canvas.SetTop(robot, SpriteTopNormal);
        Width = 96; Height = 96;
        Left = anchorX; Top = anchorY;
        ClearRegions(); UpdateRegion();
    }
    private void MouseDownRobot(object sender, MouseButtonEventArgs e)
    {
        wokeOnDown = state == PetState.Sleep;
        if (isOneEightyMinNoticeActive) { isOneEightyMinNoticeActive = false; HideBubbles(); }
        if (IsInterruptibleIdleAnimation) actionUntil = DateTime.MinValue;
        Wake(); mouseDown = true; dragging = false;
        dizzyDrag = false; rapidDistance = 0; motionStarted = DateTime.UtcNow;
        NativeMethods.GetCursorPos(out dragStart);
        previousDragPoint = dragStart;
        startLeft = Left; startTop = Top;
        robot.CaptureMouse(); e.Handled = true;
    }
    private void MouseMoveRobot(object sender, MouseEventArgs e)
    {
        if (!mouseDown || !NativeMethods.GetCursorPos(out var p)) return;
        var m = source!.CompositionTarget.TransformFromDevice;
        var delta = m.Transform(new Vector(p.X - dragStart.X, p.Y - dragStart.Y));
        if (!dragging && delta.Length < 5) return;
        if (!dragging)
        {
            dragging = true; HideBubbles(); ChangeState(PetState.Drag);
            startLeft = Left; startTop = Top; dragStart = p; delta = new Vector();
        }
        Left = startLeft + delta.X; Top = startTop + delta.Y;
        double elapsed = (DateTime.UtcNow - motionStarted).TotalSeconds;
        if (elapsed > .8) { rapidDistance = 0; motionStarted = DateTime.UtcNow; }
        rapidDistance += m.Transform(new Vector(p.X - previousDragPoint.X, p.Y - previousDragPoint.Y)).Length; previousDragPoint = p;
        if (rapidDistance > 600 && !dizzyDrag) { dizzyDrag = true; React(PetState.Dizzy, 5); }
    }
    private void MouseUpRobot(object sender, MouseButtonEventArgs e)
    {
        if (!mouseDown) return;
        bool wasDragged = dragging;
        EndDrag();
        if (!wasDragged)
        {
            rapidClicks.Add(DateTime.UtcNow);
            rapidClicks.RemoveAll(t => DateTime.UtcNow - t > TimeSpan.FromSeconds(3.5));
            if (rapidClicks.Count == 3) React(PetState.Surprise, 1);
            else if (rapidClicks.Count == 6) React(PetState.Laugh, 1);
            else if (rapidClicks.Count == 9) React(PetState.Think, 1);
            else if (rapidClicks.Count >= 10)
            {
                rapidClicks.Clear();
                React(PetState.Joy, 3);
                ShowFaceHeart(3);
                OpenCompanionChat();
                e.Handled = true;
                return;
            }
        }
        var clickReaction=robotClicks.Register(DateTime.UtcNow,wasDragged);
        if (clickReaction!=RobotClickReaction.None)
        {
            ShowClickReaction(clickReaction);
            e.Handled=true; return;
        }
        if (!wasDragged && localNoticeAnimation!=null) { e.Handled=true; return; }
        if (!wasDragged && !wokeOnDown)
        {
            if (menuOpen) HideBubbles(); else ShowMenu();
        }
        e.Handled = true;
    }
    internal void ShowClickReaction(RobotClickReaction reaction)
    {
        if (reaction==RobotClickReaction.None) return;
        if (localNoticeAnimation!=null) HideBubbles();
        if (reaction==RobotClickReaction.Offended)
        {
            var pending=announcements.Where(n=>n.LocalAnimation!=PetState.Twirl).ToArray(); announcements.Clear(); foreach(var notice in pending) announcements.Enqueue(notice);
        }
        if (announcements.Count>=10) return;
        ReceiveAnnouncement(new ClientNotice(reaction==RobotClickReaction.Greeting ? "Здравствуйте, меня зовут Артёмка. Я ваш персональный помощник." : "Я ещё развиваюсь… Пожалуйста, не обижай меня 🥺", "Артёмка", 10) { LocalAnimation=reaction==RobotClickReaction.Greeting ? PetState.Twirl : PetState.Offended });
    }
    private void EndDrag()
    {
        mouseDown = false; dragging = false;
        robot.ReleaseMouseCapture();
        ClampPosition(); SavePosition(); Wake();
        if (dizzyDrag) React(PetState.Dizzy, 5); else if (state != PetState.Wake) ChangeState(PetState.Idle);
    }
    private void ClampPosition()
    {
        var bounds = NativeMethods.DesktopBounds();
        Left = Math.Clamp(Left, bounds.Left - RobotX, bounds.Right - RobotX - robot.Width);
        Top = Math.Clamp(Top, bounds.Top - RobotY, bounds.Bottom - RobotY - robot.Height);
    }
    private void SavePosition()
    {
        if (diagnostics) return;
        try { settings.X = Left - (expanded ? 0 : SpriteLeft); settings.Y = Top - (expanded ? 0 : SpriteTop); settings.Save(); }
        catch (Exception ex) { Settings.Log(ex); }
    }
    internal static Button MakeButton(string title) => new()
    {
        Content = title, Style = AssistantButtonStyle,
        Padding = new Thickness(10, 2, 10, 2), FontSize = 12, Cursor = Cursors.Hand,
        Focusable = false, SnapsToDevicePixels = true,
        ContentTemplate = WrappingTemplate()
    };
    internal static readonly Style AssistantButtonStyle = (Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
          <Setter Property="Foreground" Value="#183655"/>
          <Setter Property="Background" Value="#F0F8FF"/>
          <Setter Property="BorderBrush" Value="#83B7D9"/>
          <Setter Property="BorderThickness" Value="1"/>
          <Setter Property="FontFamily" Value="Segoe UI"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Border x:Name="Card" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                        Margin="3" CornerRadius="10" Background="{TemplateBinding Background}"
                        BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
                  <Border.Effect><DropShadowEffect Color="#183655" BlurRadius="4" ShadowDepth="1" Opacity="0.22"/></Border.Effect>
                  <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="Card" Property="Background" Value="#DDF3FF"/>
                    <Setter TargetName="Card" Property="BorderBrush" Value="#329ED2"/>
                  </Trigger>
                  <Trigger Property="IsPressed" Value="True">
                    <Setter TargetName="Card" Property="Background" Value="#BDE7FA"/>
                    <Setter TargetName="Card" Property="BorderBrush" Value="#2179A8"/>
                  </Trigger>
                  <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """);
    private static DataTemplate WrappingTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
        factory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        factory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        factory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        return new DataTemplate { VisualTree = factory };
    }
    private void AddBubble(FrameworkElement element, double x, double y, double width, double height)
    {
        element.Width = width; element.Height = height;
        Canvas.SetLeft(element, x); Canvas.SetTop(element, y);
        bubbles.Add(element); canvas.Children.Add(element);
        ClearRegions();
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
    }
    private void KeepMenuVisible()
    {
        var bounds = NativeMethods.DesktopBounds();
        // The robot is the anchor; reposition only if an expanded menu would be off screen.
        Left = Math.Clamp(Left, bounds.Left, Math.Max(bounds.Left, bounds.Right - Width));
        Top = Math.Clamp(Top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - Height));
    }
    private void ShowMenu()
    {
        HideBubbles();
        double anchorX = expanded ? Left + Canvas.GetLeft(robot) : Left;
        double anchorY = expanded ? Top + Canvas.GetTop(robot) : Top;
        menuOpen = true;
        Wake();

        var visibleActions = actions.Where(a => a.Type != "exit").ToList();
        if (!diagnostics) visibleActions.Add(new("messenger", Loc.T("TrayMessenger"), "messenger"));
        if (!diagnostics && hub?.IsOnline == true) visibleActions.Add(new("emergency", Loc.T("ActionEmergency"), "emergency"));
        int count = visibleActions.Count;

        const double winW = 500;
        const double winH = 400;
        var bounds = NativeMethods.DesktopBounds();

        double spaceTop = anchorY - bounds.Top;
        double spaceBottom = bounds.Bottom - (anchorY + 96);
        double spaceLeft = anchorX - bounds.Left;
        double spaceRight = bounds.Right - (anchorX + 96);

        bool nearBottom = !diagnostics && spaceBottom < 170;
        bool nearTop = !diagnostics && spaceTop < 170;
        bool nearRight = !diagnostics && spaceRight < 180;
        bool nearLeft = !diagnostics && spaceLeft < 180;

        double robotCanvasX = SpriteLeft;
        double robotCanvasY = SpriteTopMenu;

        if (nearRight)
        {
            robotCanvasX = Math.Clamp(winW - 96 - Math.Max(0, spaceRight), SpriteLeft, winW - 96 - 8);
        }
        else if (nearLeft)
        {
            robotCanvasX = Math.Clamp(Math.Max(0, spaceLeft), 8, SpriteLeft);
        }

        if (nearBottom)
        {
            robotCanvasY = Math.Clamp(winH - 96 - Math.Max(0, spaceBottom), SpriteTopMenu, winH - 96 - 8);
        }
        else if (nearTop)
        {
            robotCanvasY = Math.Clamp(Math.Max(0, spaceTop), 8, SpriteTopMenu);
        }

        expanded = true;
        canvas.RenderTransform = Transform.Identity;
        Canvas.SetLeft(robot, robotCanvasX);
        Canvas.SetTop(robot, robotCanvasY);
        Left = anchorX - robotCanvasX;
        Top = anchorY - robotCanvasY;
        Width = winW;
        Height = winH;

        var (boxes, actualBtnWidth, actualBtnHeight) = ButtonLayoutEngine.CalculateLayout(
            count, robotCanvasX, robotCanvasY, winW, winH, nearBottom, nearTop, nearRight, nearLeft);

        for (int i = 0; i < count; i++)
        {
            var box = boxes[i];
            var action = visibleActions[i];
            var button = MakeButton(action.Title);
            button.ToolTip = action.Title;
            button.Click += async (_, _) => await ExecuteAction(action);
            AddBubble(button, box.X, box.Y, box.Width, box.Height);
        }

        ClearRegions();
        UpdateRegion();
        previousLeft = NativeMethods.GetAsyncKeyState(1) < 0;
        outsideClick.Start();
    }
    private void OpenSuperAdminWindow()
    {
        if (hub?.IsOnline != true || !superAvailable) return;
        if (superWindow != null) { superWindow.Activate(); return; }
        try { superWindow = new SuperAdminWindow(settings, this) { Owner = this, Topmost = true }; superWindow.Closed += (_, _) => superWindow = null; superWindow.Show(); superWindow.Activate(); }
        catch (Exception ex) { Settings.Log(ex); ShowNotice(ex.Message); }
    }
    private void HideBubbles()
    {
        if (urgentVisible) { urgentVisible = false; Topmost = settings.DisplayMode == "Topmost"; NativeMethods.SetWindowPos(handle, Topmost ? new nint(-1) : new nint(-2), 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE); ApplyDisplayMode(); if (settings.AssistantHidden) { Hide(); animation.Stop(); } }
        localNoticeAnimation = null;
        isOneEightyMinNoticeActive = false;
        announcementVisible = false;
        outsideClick.Stop();
        foreach (var bubble in bubbles) canvas.Children.Remove(bubble);
        bubbles.Clear(); ClearRegions(); menuOpen = false; Collapse(); UpdateRegion();
    }
    private void ShowNotice(string text)
    {
        HideBubbles(); Expand(); menuOpen = true;
        var button = MakeButton(text + "\n[Закрыть]");
        button.Click += (_, _) => HideBubbles();
        AddBubble(button, 90, 182, 320, 92); KeepMenuVisible(); UpdateRegion();
        previousLeft = NativeMethods.GetAsyncKeyState(1) < 0; outsideClick.Start();
    }
    internal string ReceiveAnnouncement(ClientNotice notice)
    {
        if (string.IsNullOrWhiteSpace(notice.Text) || notice.Text.Length > 1000 || notice.DurationSeconds is < 10 or > 300) throw new ArgumentException("Неверное сообщение");
        if (announcements.Count >= 10) throw new InvalidOperationException("Очередь сообщений помощника заполнена");
        announcements.Enqueue(notice);
        if (!announcementVisible && !mouseDown) { ShowNextAnnouncement(); return "Сообщение показано в облачке помощника"; }
        return "Сообщение принято в очередь показа";
    }
    internal void AdvanceAnnouncements()
    {
        if (announcementVisible && DateTime.UtcNow >= announcementUntil) HideBubbles();
        if (!announcementVisible && announcements.Count > 0 && !mouseDown) ShowNextAnnouncement();
    }
    private void ShowNextAnnouncement()
    {
        if (announcements.Count == 0) return;
        var notice = announcements.Dequeue();
        if (notice.ExpiresAt < DateTime.UtcNow) { ShowNextAnnouncement(); return; }
        HideBubbles();
        if (notice.UrgentMessageId is long urgentId) { settings.UrgentNotificationsShown.Add(notice.UrgentKey ?? (chatUserId + ":" + urgentId)); settings.UrgentNotificationsShown = settings.UrgentNotificationsShown.TakeLast(1000).ToList(); if (!diagnostics) settings.Save(); urgentVisible = true; ShowActivated = false; Show(); animation.Start(); Topmost = true; NativeMethods.SetWindowPos(handle, new nint(-1), 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE); }
        Expand(); Wake(); menuOpen = true; announcementVisible = true;
        localNoticeAnimation=notice.LocalAnimation;
        React(notice.LocalAnimation ?? PetState.Notice, notice.LocalAnimation == PetState.Twirl ? 1.2 : notice.LocalAnimation == null ? 3 : 6);
        var panel = new Grid { Margin = new Thickness(12) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(new TextBlock { Text = notice.Sender, FontWeight = FontWeights.Bold, Foreground = (Brush)new BrushConverter().ConvertFromString("#4F46E5")!, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 6) });
        var scroll = new ScrollViewer { Content = new TextBlock { Text = notice.Text, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)new BrushConverter().ConvertFromString("#1E293B")!, FontSize = 14 }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); panel.Children.Add(scroll);
        var close = MakeButton(Loc.T("Close")); close.Margin = new Thickness(0, 8, 0, 0); close.Click += (_, _) => { HideBubbles(); ShowNextAnnouncement(); }; Grid.SetRow(close, 2); panel.Children.Add(close);
        if (notice.IsDiskAlert)
        {
            var diskTicket = MakeButton(Loc.T("DiskSpaceLowTicketTitle"));
            diskTicket.Margin = new Thickness(0, 8, 8, 0);
            diskTicket.Click += (_, _) => { HideBubbles(); OpenLowDiskTicket(); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Remove(close);
            buttons.Children.Add(diskTicket);
            buttons.Children.Add(close);
            Grid.SetRow(buttons, 2);
            panel.Children.Add(buttons);
        }
        else if (notice.ChatPeerId is int peer)
        {
            close.Content = "Открыть переписку";
            close.Click += async (_, _) => await OpenMessengerAsync(peer);
        }
        else if (notice.UrgentMessageId is long messageId)
        {
            close.Content = "Открыть чат";
            var acknowledge = MakeButton("Прочитано"); acknowledge.Margin = new Thickness(0,8,0,0);
            acknowledge.Click += async (_,_) => { acknowledge.IsEnabled = false; try { if (messenger != null) { if (notice.ChatPeerId < 0) await messenger.ReadAsync(notice.ChatPeerId.Value, messageId); else await messenger.AcknowledgeAsync(messageId); } HideBubbles(); ShowNextAnnouncement(); } catch(Exception ex) { acknowledge.IsEnabled = true; Settings.Log(ex); acknowledge.Content = "Повторить подтверждение"; } };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal }; panel.Children.Remove(close); buttons.Children.Add(acknowledge); buttons.Children.Add(close); Grid.SetRow(buttons,2); panel.Children.Add(buttons);
        }
        AddBubble(new Border { Background = Brushes.White, BorderBrush = (Brush)new BrushConverter().ConvertFromString("#E2E8F0")!, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(16), Child = panel, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.16, Color = Color.FromRgb(15, 23, 42) } }, 70, 48, 360, 216);
        var tail = new System.Windows.Shapes.Polygon { Fill = Brushes.White, Stroke = (Brush)new BrushConverter().ConvertFromString("#E2E8F0")!, StrokeThickness = 1.5, Points = new PointCollection { new(0, 0), new(22, 0), new(11, 22) } };
        AddBubble(tail, SpriteLeft + 36, 262, 22, 22);
        announcementUntil = DateTime.UtcNow.AddSeconds(notice.DurationSeconds); KeepMenuVisible(); UpdateRegion();
    }
    private async Task OpenMessengerAsync(int? peer = null)
    {
        if (messenger == null) throw new InvalidOperationException("Мессенджер недоступен. Проверьте HTTPS сервера.");
        if (messengerWindow == null)
        {
            messengerWindow = new MessengerWindow(messenger, settings);
            messengerWindow.EmojiInserted += InsertEmojiReaction;
            messengerWindow.Closed += (_, _) => messengerWindow = null;
            messengerWindow.Show();
        }
        if (messengerWindow.WindowState == WindowState.Minimized) messengerWindow.WindowState = WindowState.Normal;
        messengerWindow.Activate();
        if (peer != null && peer.Value != 0 && messenger.SignedIn) await messengerWindow.OpenPeerAsync(peer.Value);
    }
    private void CheckUnreadReminder()
    {
        if (unreadChatsCount <= 0 || messenger == null || !messenger.SignedIn) return;
        if (messengerWindow != null && messengerWindow.IsActive && messengerWindow.IsVisible) return;
        if (settings.ChatDoNotDisturb && !settings.ChatUrgentOverridesQuiet || !ChatDesktop.Unlocked() || settings.AssistantHidden) return;

        if (announcements.Count < 10)
        {
            ReceiveAnnouncement(new ClientNotice($"У вас есть непрочитанные сообщения ({unreadChatsCount}) ✉️ Нажмите, чтобы открыть", "Мессенджер", 15) { ChatPeerId = 0 });
        }
        React(PetState.Celebrate, 5);
        ShowFaceMail(5);
        if (settings.ChatSound && DateTime.UtcNow - lastChatSound > TimeSpan.FromSeconds(3))
        {
            SoundManager.Play(SoundEvent.MessageReceived, settings);
            lastChatSound = DateTime.UtcNow;
        }
    }
    private async Task CheckChatAsync()
    {
        if (refreshingChat || messenger?.SignedIn != true || lifetime.IsCancellationRequested) return;
        refreshingChat = true;
        try
        {
            if (chatUserId != messenger.UserId) { chatUserId = messenger.UserId; chatUnread.Clear(); urgentShown.Clear(); dances.Clear(); pets.Clear(); }
            await Task.Delay(1500, lifetime.Token); // Coalesce a burst of messages and read receipts.
            var users = await messenger.UsersAsync();
            if (ChatDesktop.Unlocked()) foreach (var urgent in await messenger.UrgentAsync())
            {
                string key = messenger.UserId + ":" + urgent.RecipientId + ":" + urgent.Id;
                if (urgent.Command == "dance" && !settings.ChatCommandsShown.Contains(key) && !dances.Any(d => d.Key == key) && dances.Count < 50)
                {
                    dances.Enqueue((key, urgent.SentAt.ToUniversalTime().AddHours(1)));
                    TriggerDance(5);
                }
                if (urgent.Command == "pet" && !settings.ChatCommandsShown.Contains(key) && !pets.Any(d => d.Key == key) && pets.Count < 50)
                {
                    pets.Enqueue((key, urgent.SentAt.ToUniversalTime().AddHours(1)));
                    TriggerPet(6);
                }
                if (!urgent.IsUrgent || string.IsNullOrWhiteSpace(urgent.Body)) continue;
                if (urgentShown.Contains(key) || settings.UrgentNotificationsShown.Contains(key) || announcements.Count >= 10) continue;
                var sender = users.FirstOrDefault(u => u.Id == urgent.SenderId);
                ReceiveAnnouncement(new ClientNotice("СРОЧНО · " + HelperEmojis.PlainText(urgent.Body[..Math.Min(970,urgent.Body.Length)]), sender?.FullName ?? "Сотрудник", 60, urgent.RecipientId < 0 ? urgent.RecipientId : urgent.SenderId) { UrgentMessageId = urgent.Id, UrgentKey = key, ExpiresAt = urgent.SentAt.ToUniversalTime().AddHours(1) });
                urgentShown.Add(key);
                React(PetState.Celebrate, 5);
                ShowFaceMail(5);
                if (settings.ChatSound && DateTime.UtcNow - lastChatSound > TimeSpan.FromSeconds(3)) { SoundManager.Play(SoundEvent.Urgent, settings); lastChatSound = DateTime.UtcNow; }
            }
            int prevUnread = unreadChatsCount;
            unreadChatsCount = users.Count(u => u.Unread > 0);
            if (prevUnread > 0 && unreadChatsCount == 0)
            {
                React(PetState.Joy, 2);
            }
            Draw();
            tray?.SetUnread(unreadChatsCount > 0);
            var notifications = new List<(string Title, string Text, int Peer)>();
            foreach (var user in users)
            {
                long previous = chatUnread.GetValueOrDefault(user.Id); if(user.Muted) { chatUnread[user.Id]=user.LastId??0; continue; }
                if (settings.ChatDoNotDisturb && !settings.ChatUrgentOverridesQuiet || !ChatDesktop.Unlocked()) continue;
                long latestId = user.LastId ?? 0;
                if (user.Unread == 0 || messengerWindow?.ActivePeer == user.Id) { chatUnread[user.Id] = latestId; continue; }
                if (latestId <= previous) continue;
                var recent = await messenger.HistoryAsync(user.Id);
                var last = recent.LastOrDefault(m => (user.IsGroup ? m.SenderId != messenger.UserId : m.RecipientId == messenger.UserId) && m.ReadAt == null);
                chatUnread[user.Id] = latestId;
                if (last == null || last.Id <= previous || last.IsUrgent || string.IsNullOrWhiteSpace(last.Body)) continue;
                if (settings.ChatDoNotDisturb && !last.IsUrgent) continue;
                ReceiveEmoji(last);
                string text = (last.IsUrgent ? "СРОЧНО · " : "") + "Непрочитанных сообщений: " + user.Unread;
                if (settings.ChatPreview)
                {
                    text += "\n" + (user.IsGroup ? last.SenderName + ": " : "") + HelperEmojis.PlainText(last.Body[..Math.Min(300, last.Body.Length)]);
                }
                if (settings.ChatComicNotifications && !settings.AssistantHidden && announcements.Count < 10) ReceiveAnnouncement(new(text, user.FullName, 15, user.Id));
                notifications.Add((user.FullName, text, user.Id));
            }
            if (notifications.Count > 0)
            {
                React(PetState.Joy, 4);
                ShowFaceMail(4);
                bool play = settings.ChatSound && DateTime.UtcNow - lastChatSound > TimeSpan.FromSeconds(3);
                if (settings.ChatWindowsNotifications && tray != null)
                {
                    if (notifications.Count == 1) { var notice = notifications[0]; tray.Notify(notice.Title, notice.Text, notice.Peer, play); }
                    else tray.Notify("Новые сообщения · " + notifications.Count + " чатов", string.Join("\n", notifications.Take(3).Select(n => n.Title)), 0, play);
                }
                else if (play)
                { SoundManager.Play(SoundEvent.MessageReceived, settings); lastChatSound = DateTime.UtcNow; }
                if (play) lastChatSound = DateTime.UtcNow;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Settings.Log(ex); }
        finally { refreshingChat = false; }
    }
    private async Task RefreshActions()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            if (hub?.IsOnline == true) await hub.RefreshButtons();
            else { actions = ApiClient.Defaults(); if (menuOpen && !announcementVisible) ShowMenu(); }
        }
        catch (Exception ex) { Settings.Log(ex); actions = ApiClient.Defaults(); if (menuOpen && !announcementVisible) ShowMenu(); }
        finally { refreshing = false; }
    }
    internal void ShowForbiddenNotice()
    {
        Wake();
        HideBubbles();
        string msg = Loc.T("FeatureForbiddenByParent");
        ShowNotice(msg);
        React(PetState.Sad, 4);
        SoundManager.Play(SoundEvent.MessageReceived, settings);
    }

    private Task ExecuteAction(AssistantAction action)
    {
        HideBubbles(); Wake(); actionUntil = DateTime.UtcNow.AddSeconds(2); ChangeState(PetState.Action);
        try
        {
            switch (action.Type)
            {
                case "companion":
                    if (!settings.AllowCompanionChat) { ShowForbiddenNotice(); return Task.CompletedTask; }
                    OpenCompanionChat(); break;
                case "forbidden" or "disabled":
                    ShowForbiddenNotice(); return Task.CompletedTask;
                case "messenger": return OpenMessengerAsync();
                case "emergency": return OpenEmergencyDialogAsync();
                case "exit": Application.Current.Shutdown(); break;
                case "it_ticket":
                    if (hub?.IsOnline != true) throw new InvalidOperationException("Сервер недоступен. Заявка не отправлена.");
                    if (ticket != null) return Task.CompletedTask;
                    ticket = new TicketWindow(hub.CreateTicketAtAsync, hub.GetBranchesAsync, settings, lifetime.Token) { Owner = this, Topmost = true };
                    ticket.TicketCreated += id => { ticket?.Close(); ShowNotice($"Заявка №{id} успешно создана!"); };
                    ticket.Submitting += () => ChangeState(PetState.Busy);
                    ticket.SubmissionFailed += () => React(PetState.Error, 4);
                    ticket.TicketCreated += _ => React(PetState.Success, 4);
                    ticket.Closed += (_, _) => { ticket = null; Wake(); if (state == PetState.Busy) ChangeState(PetState.Idle); };
                    ticket.Show();
                    ticket.Activate();
                    ticket.Focus();
                    break;
                case "open_path":
                    var path = Environment.ExpandEnvironmentVariables(action.Target ?? "");
                    if (!Path.IsPathFullyQualified(path) || (!File.Exists(path) && !Directory.Exists(path)))
                        throw new FileNotFoundException("Путь недоступен: " + path);
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); break;
                case "open_url":
                    if (!Uri.TryCreate(action.Target, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "search-ms"))
                        throw new InvalidOperationException("Недопустимый адрес.");
                    if (uri.Scheme == "search-ms")
                    {
                        var search = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                        search.ArgumentList.Add("search-ms:"); Process.Start(search);
                    }
                    else Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); break;
                case "run_command":
                    if (!settings.AllowRemoteCommands)
                    {
                        ShowForbiddenNotice();
                        return Task.CompletedTask;
                    }
                    string executable = action.Target ?? "";
                    string[] arguments = action.Arguments ?? [];
                    if (executable.TrimStart().StartsWith('{'))
                    {
                        using var payload = System.Text.Json.JsonDocument.Parse(executable);
                        executable = payload.RootElement.GetProperty("executable").GetString()!;
                        if (payload.RootElement.TryGetProperty("arguments", out var args)) arguments = args.EnumerateArray().Select(a => a.GetString()!).ToArray();
                    }
                    executable = Environment.ExpandEnvironmentVariables(executable);
                    if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Команда должна содержать полный путь к EXE.");
                    var start = new ProcessStartInfo(executable) { UseShellExecute = false };
                    foreach (string arg in arguments) start.ArgumentList.Add(arg);
                    Process.Start(start); break;
            }
        }
        catch (Exception ex) { Settings.Log(ex); ShowNotice(ex.Message); }
        return Task.CompletedTask;
    }

    private Task OpenEmergencyDialogAsync()
    {
        if (hub?.IsOnline != true)
        {
            ShowNotice("Сервер недоступен для отправки тревоги.");
            return Task.CompletedTask;
        }
        var dlg = new EmergencyDialogWindow(hub, settings) { Owner = this, Topmost = true };
        dlg.ShowDialog();
        return Task.CompletedTask;
    }

    private void HandleCartridgeReady(CartridgeReadyNotice notice)
    {
        Wake();
        React(PetState.Joy, 5);
        SoundManager.Play(SoundEvent.MessageReceived, settings);
        string title = Loc.T("CartridgeReadyTitle");
        string body = Loc.T("CartridgeReadyBody", notice.Username, notice.Marker, notice.Model, notice.Cabinet, notice.ItOffice);
        ReceiveAnnouncement(new ClientNotice($"{title}\n\n{body}", "Склад картриджей", 25) { LocalAnimation = PetState.Joy });
    }

    private void HandleTicketReply(TicketReplyNotice notice)
    {
        Wake();
        React(PetState.Surprise, 5);
        SoundManager.Play(SoundEvent.MessageReceived, settings);
        string title = Loc.T("GlpiReplyTitle", notice.GlpiId);
        string by = Loc.T("GlpiReplyBy", notice.Author);
        ReceiveAnnouncement(new ClientNotice($"{title}\n{by}\n\n{notice.Text}", "GLPI HelpDesk", 25) { LocalAnimation = PetState.Surprise });
    }

    private void HandleEmergencyAlert(EmergencyAlertNotice notice)
    {
        StartEmergencySequence(notice, isTest: false);
    }

    internal void StartEmergencySequence(EmergencyAlertNotice notice, bool isTest = false)
    {
        Wake();
        try { activeAlertWindow?.Close(); } catch { }
        SoundManager.Play(SoundEvent.Urgent, settings);

        string code = notice.Code.ToUpperInvariant();
        bool isHighAlert = code is "CODE_RED" or "CODE_BLACK" or "CODE_ORANGE";
        bool isPinkAlert = code is "CODE_PINK";

        if (isHighAlert || isPinkAlert)
        {
            if (isPinkAlert)
            {
                // Panic animation: rounded eyes, panic notice
                React(PetState.Surprise, 3.5);
                ShowNotice(isTest
                    ? "🌸 ТЕСТ: КОД РОЗОВЫЙ (ПАНИКА / ПОИСК РЕБЁНКА)!"
                    : "🌸 ТРЕВОГА! КОД РОЗОВЫЙ! ПОИСК И СПАСЕНИЕ РЕБЁНКА!");
            }
            else
            {
                // Alarm animation: alarm state, siren, urgency notice
                React(PetState.Surprise, 3.5);
                ShowNotice(isTest
                    ? $"🚨 ТЕСТОВАЯ ТРЕВОГА! АКТИВАЦИЯ: {notice.Title}"
                    : $"🚨 ВНИМАНИЕ! ТРЕВОГА! {notice.Title}");
            }

            var preTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            preTimer.Tick += (_, _) =>
            {
                preTimer.Stop();
                ShowEmergencyAlertWindow(notice, isTest);
            };
            preTimer.Start();
        }
        else
        {
            ShowEmergencyAlertWindow(notice, isTest);
        }
    }

    private void ShowEmergencyAlertWindow(EmergencyAlertNotice notice, bool isTest)
    {
        Wake();
        try { activeAlertWindow?.Close(); } catch { }
        var window = new EmergencyAlertWindow(notice, accepted =>
        {
            if (notice.CallId.HasValue && hub != null && !isTest)
            {
                _ = hub.AcknowledgeSpecialistCallAsync(notice.CallId.Value, accepted);
            }
        }, isTest, onCancel: () => CancelActiveEmergency(isTest));

        activeAlertWindow = window;
        window.Closed += (_, _) => { if (ReferenceEquals(activeAlertWindow, window)) activeAlertWindow = null; };
        window.Show();
        window.Activate();
    }

    internal void RunEmergencyTest(string code)
    {
        string upper = code.Trim().ToUpperInvariant();
        string title = upper switch
        {
            "CODE_RED" => "ТЕСТ: КОД КРАСНЫЙ (Пожар / Задымление)",
            "CODE_BLACK" => "ТЕСТ: КОД ЧЁРНЫЙ (Теракт / Угроза взрыва)",
            "CODE_ORANGE" => "ТЕСТ: КОД ОРАНЖЕВЫЙ (Опасные вещества / Авария)",
            "CODE_YELLOW" => "ТЕСТ: КОД ЖЁЛТЫЙ (Чрезвычайная ситуация)",
            "CODE_BLUE" => "ТЕСТ: КОД СИНИЙ (Остановка сердца / СЛР)",
            "CODE_WHITE" => "ТЕСТ: КОД БЕЛЫЙ (Агрессия / Нападение)",
            "CODE_PINK" => "ТЕСТ: КОД РОЗОВЫЙ (Потеря / Похищение ребёнка)",
            _ => $"ТЕСТ: {upper}"
        };
        string notes = "Проверка алгоритма действий и блокировки экрана на ПК администратора. Реальная рассылка по сети и в Telegram отключена.";
        var testNotice = new EmergencyAlertNotice(upper, title, settings.TicketRoom ?? "Кабинет IT", notes, null, null, null, upper == "CODE_PINK" ? 60 : 45, null, null);
        StartEmergencySequence(testNotice, isTest: true);
    }

    internal void CancelActiveEmergency(bool isTest = false)
    {
        Wake();
        if (activeAlertWindow != null)
        {
            try { activeAlertWindow.Close(); } catch { }
            activeAlertWindow = null;
        }
        if (!isTest && hub?.IsOnline == true)
        {
            _ = hub.CancelEmergencyAlertAsync();
        }
        ShowNotice(isTest
            ? "🟢 Тестирование завершено: оповещение сброшено"
            : "🟢 Отбой тревоги: оповещение сброшено");
        React(PetState.Joy, 3);
    }

    private void HandleEmergencyAlertCanceled()
    {
        Wake();
        if (activeAlertWindow != null)
        {
            try { activeAlertWindow.Close(); } catch { }
            activeAlertWindow = null;
            ShowNotice("🟢 Отбой тревоги: оповещение сброшено");
            React(PetState.Joy, 3);
        }
    }

    private async void TriggerDance(int durationSec = 5)
    {
        SoundManager.Play(SoundEvent.Dance, settings);
        int delay = SoundManager.GetDanceLeadDelayMs(settings.SoundProfile);
        if (delay > 0)
        {
            await Task.Delay(delay);
        }
        React(PetState.Dance, durationSec);
    }

    private static readonly Random petRandom = new();
    internal void TriggerPet(int durationSec = 6)
    {
        bool isCat = petRandom.Next(2) == 0;
        PetState selected = isCat ? PetState.PetCat : PetState.PetDog;
        React(selected, durationSec);
        ShowNotice(Loc.T(isCat ? "NoticePetCat" : "NoticePetDog"));
        SoundManager.Play(SoundEvent.MessageReceived, settings);
    }

    private void LoadBirthdayHat()
    {
        try
        {
            string baseDir = AppContext.BaseDirectory;
            string hatPath = Path.Combine(baseDir, "Assets", "hat_birthday.png");
            if (!File.Exists(hatPath))
            {
                string? cur = baseDir;
                while (cur != null)
                {
                    string candidate = Path.Combine(cur, "Assets", "hat_birthday.png");
                    if (File.Exists(candidate)) { hatPath = candidate; break; }
                    string candidate2 = Path.Combine(cur, "LitleHelperClient", "Assets", "hat_birthday.png");
                    if (File.Exists(candidate2)) { hatPath = candidate2; break; }
                    cur = Path.GetDirectoryName(cur);
                }
            }
            if (File.Exists(hatPath))
            {
                var uri = new Uri(hatPath, UriKind.Absolute);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = uri;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                birthdayHatSource = bmp;
            }
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    private void UpdateBirthdayHat()
    {
        DateTime now = DateTime.Now;
        bool isBirthday = (now.Month == 10 && now.Day == 1) ||
                          (now.Month == settings.FirstStartupDate.Month && now.Day == settings.FirstStartupDate.Day);
        if (isBirthday && birthdayHatSource != null)
        {
            int bob = GetHeadBob(state, frame);
            Canvas.SetLeft(hatOverlay, SpriteLeft + 8 * Scale);
            Canvas.SetTop(hatOverlay, SpriteTop + (bob - 4) * Scale);
            hatOverlay.Source = birthdayHatSource;
            hatOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            hatOverlay.Visibility = Visibility.Collapsed;
        }
    }

    internal void OpenCompanionChat()
    {
        if (!settings.AllowCompanionChat)
        {
            ShowForbiddenNotice();
            return;
        }
        if (companionWindow != null && companionWindow.IsVisible)
        {
            companionWindow.Activate();
            companionWindow.Focus();
            return;
        }
        companionWindow = new CompanionChatWindow(settings, sprites, (reactState, sec) =>
        {
            React(reactState, sec);
        });
        companionWindow.Closed += (_, _) => { companionWindow = null; };
        companionWindow.Show();
        companionWindow.Activate();
    }

    private void OpenLowDiskTicket()
    {
        if (hub?.IsOnline != true)
        {
            ShowNotice(Loc.T("DiskSpaceLowWarning"));
            return;
        }
        if (ticket != null) return;
        ticket = new TicketWindow(hub.CreateTicketAtAsync, hub.GetBranchesAsync, settings, lifetime.Token, Loc.T("DiskSpaceLowTicketTitle"))
        {
            Owner = this,
            Topmost = true
        };
        ticket.TicketCreated += id => { ticket?.Close(); ShowNotice($"Заявка №{id} успешно создана!"); };
        ticket.Submitting += () => ChangeState(PetState.Busy);
        ticket.SubmissionFailed += () => React(PetState.Error, 4);
        ticket.TicketCreated += _ => React(PetState.Success, 4);
        ticket.Closed += (_, _) => { ticket = null; Wake(); if (state == PetState.Busy) ChangeState(PetState.Idle); };
        ticket.Show();
        ticket.Activate();
        ticket.Focus();
    }

    private void CheckImageViewer()
    {
        if (diagnostics || mouseDown || menuOpen || ticket != null || state == PetState.Charging || (IsAnimationLocked && !imageReactionActive)) return;

        if (imageReactionActive)
        {
            if (DateTime.UtcNow < imageReactionPhase1Until)
            {
                return;
            }
            if (DateTime.UtcNow < imageReactionPhase2Until)
            {
                if (state != PetState.Shy)
                {
                    ChangeState(PetState.Shy);
                    actionUntil = imageReactionPhase2Until;
                }
                return;
            }

            imageReactionActive = false;
            if (state == PetState.Shy)
            {
                ChangeState(PetState.Idle);
            }
            return;
        }

        var fg = NativeMethods.GetForegroundWindow();
        if (fg == 0 || fg == handle) return;

        if (fg != lastViewerHwnd)
        {
            double robotCenterX = Left + RobotX + 48;
            if (NativeMethods.IsImageViewerWindow(fg, robotCenterX, out bool isFullscreen, out bool onLeft))
            {
                lastViewerHwnd = fg;
                imageReactionActive = true;
                imageReactionPhase1Until = DateTime.UtcNow.AddSeconds(5);
                imageReactionPhase2Until = DateTime.UtcNow.AddSeconds(10);
                actionUntil = imageReactionPhase2Until;

                PetState turnState = isFullscreen ? PetState.TurnBack : (onLeft ? PetState.LookLeft : PetState.LookRight);
                ChangeState(turnState);
            }
            else
            {
                lastViewerHwnd = 0;
            }
        }
    }
}



