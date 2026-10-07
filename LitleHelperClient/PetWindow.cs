using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PixelHelper;

public sealed class PetWindow : Window
{
    private const double SpriteLeft = 202, SpriteTop = 286, Scale = 2;
    private readonly Canvas canvas = new();
    private readonly Image robot = new() { Width = 96, Height = 96, Cursor = Cursors.Hand };
    private readonly List<FrameworkElement> bubbles = [];
    private readonly Settings settings = Settings.Load();
    private readonly RobotClicks robotClicks = new();
    private PetState? localNoticeAnimation;
    private HubConnectionService? hub;
    private readonly Sprites sprites = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer animation = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer inactivity = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer refresh = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(30) };
    private readonly DispatcherTimer outsideClick = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
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
    private DateTime announcementUntil;
    private readonly bool diagnostics;
    private bool expanded = true;
    internal int AnimatedFrames { get; private set; }
    private double RobotX => expanded ? SpriteLeft : 0;
    private double RobotY => expanded ? SpriteTop : 0;

    public PetWindow(bool diagnostics = false)
    {
        this.diagnostics = diagnostics;
        emojiReactions = new EmojiReactions(diagnostics ? null : Path.Combine(Settings.Folder, "emoji-seen.json"));
        Width = 500; Height = 400;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = null;
        ShowInTaskbar = false;
        ShowActivated = false;
        if (settings.DisplayMode is not ("Topmost" or "Normal" or "Background")) settings.DisplayMode = "Background";
        if (diagnostics) settings.DisplayMode = "Background";
        Topmost = !diagnostics && settings.DisplayMode == "Topmost";
        Title = "PixelHelper";
        Content = canvas;
        actions = ApiClient.Defaults();
        if (!diagnostics)
        {
            try
            {
                messenger = new MessengerClient(settings);
                messenger.Changed += () => Dispatcher.BeginInvoke(new Action(async () => await CheckChatAsync()));
                messenger.MessageReceived += message => Dispatcher.BeginInvoke(new Action(async () => { ReceiveEmoji(message); await CheckChatAsync(); }));
            }
            catch (Exception ex) { Settings.Log(ex); }
            InitializeHub();
        }
        RenderOptions.SetBitmapScalingMode(robot, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(robot, SpriteLeft); Canvas.SetTop(robot, SpriteTop);
        canvas.Children.Add(robot);
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
            AddDisplayMenu(menu);
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
            if (!diagnostics) hub?.Start();
            if (!diagnostics && messenger != null) _ = messenger.StartAsync();
            if (!diagnostics) React(PetState.Greeting, 3);
            if (!diagnostics) { Settings.PrepareStartup(); var greeting = await Task.Run(UserGreeting.Text); if (!Dispatcher.HasShutdownStarted) { ReceiveAnnouncement(new ClientNotice(greeting, "PixelHelper", 10)); React(PetState.Greeting, 3); } }
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
            AdvanceEmoji();
            ApplyDisplayMode();
            bool exposed = IsExposed();
            if (exposed && !animation.IsEnabled) animation.Start();
            else if (!exposed && animation.IsEnabled) animation.Stop();
            if (!emojiAnimating && !menuOpen && !mouseDown && ticket == null && state is not (PetState.Sleep or PetState.Yawn) &&
                (DateTime.UtcNow - lastInteraction > TimeSpan.FromMinutes(5) || NativeMethods.IdleTime() > TimeSpan.FromMinutes(5)))
                React(PetState.Yawn, 2);
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
        if (message == NativeMethods.WM_WINDOWPOSCHANGING && settings.DisplayMode == "Background")
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
    private void ApplyDisplayMode()
    {
        if (handle == 0) return;
        if (settings.DisplayMode == "Background") NativeMethods.Bottom(handle);
    }
    internal void SetDisplayMode(string mode)
    {
        if (mode is not ("Topmost" or "Normal" or "Background")) throw new ArgumentException("Неизвестный режим.");
        settings.DisplayMode = mode; Topmost = mode == "Topmost";
        NativeMethods.SetWindowPos(handle, mode == "Topmost" ? new nint(-1) : new nint(-2), 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        ApplyDisplayMode(); if (!diagnostics) settings.Save();
    }
    internal void SetAssistantHidden(bool hidden)
    {
        settings.AssistantHidden = hidden; if (hidden) { HideBubbles(); Hide(); animation.Stop(); } else { Show(); ApplyDisplayMode(); }
        if (!diagnostics) settings.Save();
    }
    internal void RestoreDisplayPreferences()
    {
        if (diagnostics) return;
        SetDisplayMode(settings.DisplayMode);
        if (settings.AssistantHidden) SetAssistantHidden(true);
    }
    private void AddDisplayMenu(ContextMenu menu)
    {
        var modes = new MenuItem { Header = "Отображение помощника" };
        foreach (var (value, label) in new[] { ("Topmost", "Поверх всех окон"), ("Normal", "Обычный режим"), ("Background", "Фон · под окнами") })
        { var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = settings.DisplayMode == value }; item.Click += (_, _) => SetDisplayMode(value); modes.Items.Add(item); }
        menu.Items.Add(modes);
        var hide = new MenuItem { Header = settings.AssistantHidden ? "Показать помощника" : "Скрыть помощника" }; hide.Click += (_, _) => SetAssistantHidden(!settings.AssistantHidden); menu.Items.Add(hide);
        var quiet = new MenuItem { Header = "Не беспокоить", IsCheckable = true, IsChecked = settings.ChatDoNotDisturb }; quiet.Click += (_, _) => { settings.ChatDoNotDisturb = quiet.IsChecked; settings.Save(); }; menu.Items.Add(quiet);
    }
    private void InitializeHub()
    {
            try
            {
                hub = new HubConnectionService(settings);
                hub.SuperAdminAvailable += available => Dispatcher.BeginInvoke(new Action(() => { superAvailable = available; if (menuOpen && !announcementVisible) ShowMenu(); }));
                hub.NoticeReceived += notice => Dispatcher.InvokeAsync(() => ReceiveAnnouncement(notice)).Task;
                hub.OnlineChanged += online => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!online) { React(PetState.Error, 4); actions = ApiClient.Defaults(); if (menuOpen && !announcementVisible) ShowMenu(); }
                }));
                hub.ButtonsUpdated += buttons => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (hub.IsOnline)
                    {
                        actions = buttons.Where(b => b.IsActive).OrderBy(b => b.OrderIndex).ThenBy(b => b.Id).Take(12)
                            .Select(b => new AssistantAction(b.Id.ToString(), b.Title, b.ActionType switch { "open_folder" => "open_path", "ticket" => "it_ticket", _ => b.ActionType }, b.Payload)).ToList();
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
        AddDisplayMenu(menu); menu.Items.Add(new Separator()); var exit = new MenuItem { Header = "Выход" }; exit.Click += (_, _) => Close(); menu.Items.Add(exit); menu.IsOpen = true;
    }
    private void Draw()
    {
        current = sprites.Get(state, frame);
        robot.Source = current.Image;
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
    private void ChangeState(PetState value)
    {
        if (state == value) return;
        state = value; frame = 0;
        animation.Interval = TimeSpan.FromMilliseconds(value == PetState.Twirl ? 150 : value == PetState.Offended ? 450 : value == PetState.Sleep ? 250 : 100);
        Draw();
    }
    private static bool IsTemporary(PetState value) => value is PetState.Action or PetState.Greeting or PetState.Success or PetState.Error or PetState.Notice or PetState.Yawn or PetState.Wake or PetState.Dizzy or PetState.Joy or PetState.Sad or PetState.Surprise or PetState.Laugh or PetState.Think or PetState.Celebrate or PetState.Offended or PetState.Twirl;
    internal void React(PetState value, double seconds)
    { emojiAnimating = false; actionUntil = DateTime.UtcNow.AddSeconds(seconds); ChangeState(value); }
    private bool EmojiAllowed => settings.EmojiReactions && !settings.ChatDoNotDisturb && !settings.AssistantHidden && IsVisible && (diagnostics || ChatDesktop.Unlocked());
    internal void InsertEmojiReaction(HelperEmoji emoji)
    { if (EmojiAllowed) { emojiReactions.Insert(emoji); AdvanceEmoji(); } }
    private void ReceiveEmoji(ChatEntry message)
    {
        if (messenger?.SignedIn != true || message.SenderId == messenger.UserId) return;
        emojiReactions.Incoming(messenger.IdentityContext, message, EmojiAllowed && settings.IncomingEmojiReactions); AdvanceEmoji();
    }
    private void AdvanceEmoji()
    {
        var now = DateTime.UtcNow;
        if (!EmojiAllowed) { emojiReactions.Clear(); if (emojiAnimating) { emojiAnimating = false; ChangeState(emojiRestore); } return; }
        if (mouseDown || state == PetState.Busy || state == PetState.Drag) { emojiAnimating = false; return; }
        if (emojiAnimating) { if (now < emojiUntil) return; emojiAnimating = false; ChangeState(emojiRestore); }
        if (IsTemporary(state) && now < actionUntil) return;
        var emoji = emojiReactions.Take(now); if (emoji == null) return;
        emojiRestore = state == PetState.Sleep ? PetState.Sleep : PetState.Idle;
        emojiAnimating = true; emojiUntil = now.AddSeconds(emoji.Seconds); ChangeState(emoji.State);
        if (!animation.IsEnabled && IsExposed()) animation.Start();
    }
    private void Wake() { lastInteraction = DateTime.UtcNow; if (state is PetState.Sleep or PetState.Yawn) React(PetState.Wake, 2); }
    private void FollowCursor()
    {
        if (state is not (PetState.Idle or PetState.LookLeft or PetState.LookRight or PetState.LookUp or PetState.LookDown) || mouseDown || !IsVisible || !NativeMethods.GetCursorPos(out var cursor)) return;
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
        if (expanded) return;
        double anchorX = Left, anchorY = Top;
        expanded = true;
        canvas.RenderTransform = Transform.Identity;
        Left = anchorX - SpriteLeft; Top = anchorY - SpriteTop;
        Width = 500; Height = 400;
        ClearRegions(); UpdateRegion();
    }
    private void Collapse()
    {
        if (!expanded || bubbles.Count > 0) return;
        double anchorX = Left + SpriteLeft, anchorY = Top + SpriteTop;
        expanded = false;
        canvas.RenderTransform = new TranslateTransform(-SpriteLeft, -SpriteTop);
        Width = 96; Height = 96;
        Left = anchorX; Top = anchorY;
        ClearRegions(); UpdateRegion();
    }
    private void MouseDownRobot(object sender, MouseButtonEventArgs e)
    {
        wokeOnDown = state == PetState.Sleep;
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
        HideBubbles(); Expand(); menuOpen = true; Wake();
        var visibleActions = actions.Where(a => a.Type != "exit").ToList();
        if (!diagnostics) visibleActions.Add(new("messenger", "Мессенджер", "messenger"));
        int rows = (visibleActions.Count + 1) / 2;
        for (int i = 0; i < visibleActions.Count; i++)
        {
            var action = visibleActions[i]; var button = MakeButton(action.Title);
            button.ToolTip = action.Title;
            button.Click += async (_, _) => await ExecuteAction(action);
            AddBubble(button, 54 + (i % 2) * 202, 278 - rows * 44 + (i / 2) * 44, 190, 40);
        }
        KeepMenuVisible(); UpdateRegion();
        previousLeft = NativeMethods.GetAsyncKeyState(1) < 0; outsideClick.Start();
    }
    private void OpenSuperAdminWindow()
    {
        if (hub?.IsOnline != true || !superAvailable) return;
        if (superWindow != null) { superWindow.Activate(); return; }
        try { superWindow = new SuperAdminWindow(settings); superWindow.Closed += (_, _) => superWindow = null; superWindow.Show(); }
        catch (Exception ex) { Settings.Log(ex); ShowNotice(ex.Message); }
    }
    private void HideBubbles()
    {
        localNoticeAnimation=null;
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
        var notice = announcements.Dequeue(); HideBubbles(); Expand(); Wake(); menuOpen = true; announcementVisible = true;
        localNoticeAnimation=notice.LocalAnimation;
        React(notice.LocalAnimation ?? PetState.Notice, notice.LocalAnimation == PetState.Twirl ? 1.2 : notice.LocalAnimation == null ? 3 : 6);
        var panel = new Grid { Margin = new Thickness(12) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(new TextBlock { Text = notice.Sender, FontWeight = FontWeights.Bold, Foreground = Brushes.MidnightBlue, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 6) });
        var scroll = new ScrollViewer { Content = new TextBlock { Text = notice.Text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.MidnightBlue, FontSize = 14 }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); panel.Children.Add(scroll);
        var close = MakeButton("Закрыть"); close.Margin = new Thickness(0, 8, 0, 0); close.Click += (_, _) => { HideBubbles(); ShowNextAnnouncement(); }; Grid.SetRow(close, 2); panel.Children.Add(close);
        if (notice.ChatPeerId is int peer)
        {
            close.Content = "Открыть переписку";
            close.Click += async (_, _) => await OpenMessengerAsync(peer);
        }
        AddBubble(new Border { Background = Brushes.AliceBlue, BorderBrush = Brushes.SteelBlue, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(14), Child = panel }, 70, 48, 360, 216);
        var tail = new System.Windows.Shapes.Polygon { Fill = Brushes.AliceBlue, Stroke = Brushes.SteelBlue, StrokeThickness = 2, Points = new PointCollection { new(0, 0), new(22, 0), new(11, 22) } };
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
        if (peer != null && messenger.SignedIn) await messengerWindow.OpenPeerAsync(peer.Value);
    }
    private async Task CheckChatAsync()
    {
        if (refreshingChat || messenger?.SignedIn != true || lifetime.IsCancellationRequested) return;
        refreshingChat = true;
        try
        {
            if (chatUserId != messenger.UserId) { chatUserId = messenger.UserId; chatUnread.Clear(); }
            await Task.Delay(1500, lifetime.Token); // Coalesce a burst of messages and read receipts.
            var users = await messenger.UsersAsync();
            tray?.SetUnread(users.Any(u => u.Unread > 0));
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
                if (last == null || last.Id <= previous) continue;
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
                bool play = settings.ChatSound && DateTime.UtcNow - lastChatSound > TimeSpan.FromSeconds(3);
                if (settings.ChatWindowsNotifications && tray != null)
                {
                    if (notifications.Count == 1) { var notice = notifications[0]; tray.Notify(notice.Title, notice.Text, notice.Peer, play); }
                    else tray.Notify("Новые сообщения · " + notifications.Count + " чатов", string.Join("\n", notifications.Take(3).Select(n => n.Title)), 0, play);
                }
                else if (play)
                { System.Media.SystemSounds.Asterisk.Play(); lastChatSound = DateTime.UtcNow; }
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
    private Task ExecuteAction(AssistantAction action)
    {
        HideBubbles(); Wake(); actionUntil = DateTime.UtcNow.AddSeconds(2); ChangeState(PetState.Action);
        try
        {
            switch (action.Type)
            {
                case "messenger": return OpenMessengerAsync();
                case "exit": Application.Current.Shutdown(); break;
                case "it_ticket":
                    if (hub?.IsOnline != true) throw new InvalidOperationException("Сервер недоступен. Заявка не отправлена.");
                    if (ticket != null) return Task.CompletedTask;
                    ticket = new TicketWindow(hub.CreateTicketAtAsync, hub.GetBranchesAsync, settings, lifetime.Token) { Left = Left - 135, Top = Math.Max(SystemParameters.VirtualScreenTop, Top - 410) };
                    ticket.TicketCreated += id => { ticket?.Close(); ShowNotice($"Заявка №{id} успешно создана!"); };
                    ticket.Submitting += () => ChangeState(PetState.Busy);
                    ticket.SubmissionFailed += () => React(PetState.Error, 4);
                    ticket.TicketCreated += _ => React(PetState.Success, 4);
                    ticket.Closed += (_, _) => { ticket = null; Wake(); if (state == PetState.Busy) ChangeState(PetState.Idle); };
                    ticket.Show(); break;
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
                    if (!settings.AllowRemoteCommands) throw new InvalidOperationException("Команды сервера отключены. Администратор может включить allowRemoteCommands в config.json.");
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
}
