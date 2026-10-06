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
    private readonly HubConnectionService? hub;
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
    private bool menuOpen, firstPrompt, mouseDown, dragging, wokeOnDown, previousLeft;
    private NativeMethods.POINT dragStart;
    private double startLeft, startTop;
    private bool refreshing;
    private TicketWindow? ticket;
    private readonly bool diagnostics;
    private bool expanded = true;
    internal int AnimatedFrames { get; private set; }
    private double RobotX => expanded ? SpriteLeft : 0;
    private double RobotY => expanded ? SpriteTop : 0;

    public PetWindow(bool diagnostics = false)
    {
        this.diagnostics = diagnostics;
        Width = 500; Height = 400;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = null;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;
        Title = "PixelHelper";
        Content = canvas;
        actions = ApiClient.Defaults();
        if (!diagnostics)
        {
            try
            {
                hub = new HubConnectionService(settings);
                hub.OnlineChanged += online => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!online) { actions = ApiClient.Defaults(); if (menuOpen) ShowMenu(); }
                }));
                hub.ButtonsUpdated += buttons => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (hub.IsOnline)
                    {
                        actions = buttons.Where(b => b.IsActive).OrderBy(b => b.OrderIndex).ThenBy(b => b.Id).Take(12)
                            .Select(b => new AssistantAction(b.Id.ToString(), b.Title, b.ActionType switch { "open_folder" => "open_path", "ticket" => "it_ticket", _ => b.ActionType }, b.Payload)).ToList();
                        if (menuOpen) ShowMenu();
                    }
                }));
            }
            catch (Exception ex) { Settings.Log(ex); }
        }
        RenderOptions.SetBitmapScalingMode(robot, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(robot, SpriteLeft); Canvas.SetTop(robot, SpriteTop);
        canvas.Children.Add(robot);
        robot.MouseEnter += (_, _) => Wake();
        robot.MouseLeftButtonDown += MouseDownRobot;
        robot.MouseMove += MouseMoveRobot;
        robot.MouseLeftButtonUp += MouseUpRobot;
        robot.LostMouseCapture += (_, _) => { if (mouseDown) EndDrag(); };
        SourceInitialized += InitializeNative;
        Loaded += async (_, _) =>
        {
            if (!diagnostics && !Settings.FirstRunCompleted) ShowFirstPrompt();
            animation.Start(); inactivity.Start(); refresh.Start();
            if (!diagnostics) hub?.Start();
            await Task.CompletedTask;
        };
        LocationChanged += (_, _) => { if (handle != 0) NativeMethods.Bottom(handle); };
        animation.Tick += (_, _) =>
        {
            if (state == PetState.Action && DateTime.UtcNow >= actionUntil) ChangeState(PetState.Idle);
            frame++; AnimatedFrames++; Draw();
        };
        inactivity.Tick += (_, _) =>
        {
            NativeMethods.Bottom(handle);
            bool exposed = IsExposed();
            if (exposed && !animation.IsEnabled) animation.Start();
            else if (!exposed && animation.IsEnabled) animation.Stop();
            if (!menuOpen && !firstPrompt && !mouseDown && ticket == null && state != PetState.Sleep &&
                (DateTime.UtcNow - lastInteraction > TimeSpan.FromMinutes(5) || NativeMethods.IdleTime() > TimeSpan.FromMinutes(5)))
                ChangeState(PetState.Sleep);
        };
        refresh.Tick += async (_, _) => await RefreshActions();
        outsideClick.Tick += (_, _) =>
        {
            bool down = NativeMethods.GetAsyncKeyState(1) < 0 || NativeMethods.GetAsyncKeyState(2) < 0;
            if (down && !previousLeft && NativeMethods.GetCursorPos(out var p) && NativeMethods.WindowFromPoint(p) != handle)
                HideBubbles();
            previousLeft = down;
        };
        Closed += async (_, _) =>
        {
            animation.Stop(); inactivity.Stop(); refresh.Stop(); outsideClick.Stop();
            lifetime.Cancel(); ticket?.Close(); source?.RemoveHook(Hook);
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
        NativeMethods.Bottom(handle);
        UpdateRegion();
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.WM_WINDOWPOSCHANGING)
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
        var key = (state, frame % (state == PetState.Idle ? 4 : 2), m.M11, m.M22);
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
        animation.Interval = TimeSpan.FromMilliseconds(value == PetState.Sleep ? 250 : 100);
        Draw();
    }
    private void Wake() { lastInteraction = DateTime.UtcNow; if (state == PetState.Sleep) ChangeState(PetState.Idle); }
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
        expanded = true;
        canvas.RenderTransform = Transform.Identity;
        Width = 500; Height = 400;
        Left -= SpriteLeft; Top -= SpriteTop;
        ClearRegions(); UpdateRegion();
    }
    private void Collapse()
    {
        if (!expanded || bubbles.Count > 0) return;
        expanded = false;
        canvas.RenderTransform = new TranslateTransform(-SpriteLeft, -SpriteTop);
        Left += SpriteLeft; Top += SpriteTop;
        Width = 96; Height = 96;
        ClearRegions(); UpdateRegion();
    }
    private void MouseDownRobot(object sender, MouseButtonEventArgs e)
    {
        wokeOnDown = state == PetState.Sleep;
        Wake(); mouseDown = true; dragging = false;
        NativeMethods.GetCursorPos(out dragStart);
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
    }
    private void MouseUpRobot(object sender, MouseButtonEventArgs e)
    {
        if (!mouseDown) return;
        bool wasDragged = dragging;
        EndDrag();
        if (!wasDragged && !wokeOnDown && !firstPrompt)
        {
            if (menuOpen) HideBubbles(); else ShowMenu();
        }
        e.Handled = true;
    }
    private void EndDrag()
    {
        mouseDown = false; dragging = false;
        robot.ReleaseMouseCapture();
        ClampPosition(); SavePosition(); Wake(); ChangeState(PetState.Idle);
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
        Content = title, Background = Brushes.AliceBlue, Foreground = new SolidColorBrush(Color.FromRgb(24, 54, 85)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(55, 117, 161)), BorderThickness = new Thickness(2),
        Padding = new Thickness(6), FontSize = 12, Cursor = Cursors.Hand,
        Focusable = false, SnapsToDevicePixels = true,
        ContentTemplate = WrappingTemplate()
    };
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
    private void ShowFirstPrompt()
    {
        Expand();
        firstPrompt = true;
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.Children.Add(new TextBlock { Text = "Оставить меня на рабочем столе для быстрого доступа к документам?", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.MidnightBlue });
        var choices = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (bool yes in new[] { true, false })
        {
            var button = MakeButton(yes ? "Да" : "Нет"); button.Margin = new Thickness(8, 6, 8, 0);
            button.Click += (_, _) =>
            {
                try { Settings.CompleteFirstRun(yes); }
                catch (Exception ex)
                {
                    Settings.Log(ex);
                    ((TextBlock)panel.Children[0]).Text = "Не удалось сохранить настройку: " + ex.Message;
                    return;
                }
                firstPrompt = false; HideBubbles();
                if (!yes) Close();
            };
            choices.Children.Add(button);
        }
        panel.Children.Add(choices);
        AddBubble(new Border { Background = Brushes.AliceBlue, BorderBrush = Brushes.SteelBlue, BorderThickness = new Thickness(2), Child = panel }, 110, 156, 280, 120);
        KeepMenuVisible(); UpdateRegion();
    }
    private void ShowMenu()
    {
        HideBubbles(); Expand(); menuOpen = true; Wake();
        for (int i = 0; i < actions.Count; i++)
        {
            var action = actions[i]; var button = MakeButton(action.Title);
            button.Click += async (_, _) => await ExecuteAction(action);
            AddBubble(button, 10 + (i % 3) * 162, 18 + (i / 3) * 56, 154, 48);
        }
        if (hub?.IsOnline == true)
        {
            var exit = MakeButton("Закрыть помощника"); exit.Click += (_, _) => Close();
            AddBubble(exit, 322, 292, 154, 34);
        }
        KeepMenuVisible(); UpdateRegion();
        previousLeft = NativeMethods.GetAsyncKeyState(1) < 0; outsideClick.Start();
    }
    private void HideBubbles()
    {
        if (firstPrompt) return;
        outsideClick.Stop();
        foreach (var bubble in bubbles) canvas.Children.Remove(bubble);
        bubbles.Clear(); ClearRegions(); menuOpen = false; Collapse(); UpdateRegion();
    }
    private void ShowNotice(string text)
    {
        if (firstPrompt) return;
        HideBubbles(); Expand(); menuOpen = true;
        var button = MakeButton(text + "\n[Закрыть]");
        button.Click += (_, _) => HideBubbles();
        AddBubble(button, 90, 182, 320, 92); KeepMenuVisible(); UpdateRegion();
        previousLeft = NativeMethods.GetAsyncKeyState(1) < 0; outsideClick.Start();
    }
    private async Task RefreshActions()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            if (hub?.IsOnline == true) await hub.RefreshButtons();
            else { actions = ApiClient.Defaults(); if (menuOpen) ShowMenu(); }
        }
        catch (Exception ex) { Settings.Log(ex); actions = ApiClient.Defaults(); if (menuOpen) ShowMenu(); }
        finally { refreshing = false; }
    }
    private Task ExecuteAction(AssistantAction action)
    {
        HideBubbles(); Wake(); actionUntil = DateTime.UtcNow.AddSeconds(2); ChangeState(PetState.Action);
        try
        {
            switch (action.Type)
            {
                case "exit": Application.Current.Shutdown(); break;
                case "it_ticket":
                    if (hub?.IsOnline != true) throw new InvalidOperationException("Сервер недоступен. Заявка не отправлена.");
                    if (ticket != null) return Task.CompletedTask;
                    ticket = new TicketWindow(hub.CreateTicketAsync, lifetime.Token) { Left = Left - 135, Top = Math.Max(SystemParameters.VirtualScreenTop, Top - 270) };
                    ticket.TicketCreated += id => { ticket?.Close(); ShowNotice($"Заявка №{id} успешно создана!"); };
                    ticket.Closed += (_, _) => { ticket = null; Wake(); };
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
