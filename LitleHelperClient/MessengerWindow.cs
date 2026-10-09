using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace PixelHelper;

internal sealed class MessengerWindow : Window
{
    private readonly MessengerClient client;
    private readonly Settings settings;
    private List<ChatGroupEvent> groupEvents = [];
    private int eventsPeer;
    private readonly Grid root = new() { Margin = new Thickness(0) };
    private Brush surface = Brushes.White, raised = Brushes.LightGray, ink = Brushes.Black, muted = Brushes.Gray, incoming = Brushes.White, outgoing = Brushes.LightCyan, accent = Brushes.Teal, line = Brushes.Gray;
    private ListBox? users;
    private TextBox? search, input;
    private TextBlock? error, title, chatStatus;
    private Border? chatAvatar;
    private StackPanel? history;
    private ScrollViewer? scroll;
    private Button? send;

    private StackPanel? filterChipsRow;
    private readonly List<Button> dockNavButtons = [];
    private readonly List<Button> filterChipButtons = [];
    private int peer;
    private bool updating, closing, showingLogin;
    private string? pendingId, pendingText;
    private int pendingPeer;
    private readonly List<string> pendingFiles = [];
    private readonly string clipboardDirectory = Path.Combine(Path.GetTempPath(), "PixelHelper-chat-" + Guid.NewGuid().ToString("N"));
    private WrapPanel? attachmentsPanel;
    private bool sending;
    private ChatEntry? pendingMessage;
    private bool pendingFailed;
    private StackPanel? details;
    private ScrollViewer? detailsView;
    private bool detailsOpen;
    private Dictionary<string, List<ReactionInfo>> reactions = [];
    private string chatFilter = "all";
    private Popup? emojiPopup;
    private DateTime typingSent;

    internal event Action<HelperEmoji>? EmojiInserted;
    internal int ActivePeer => IsActive ? peer : 0;

    internal MessengerWindow(MessengerClient client, Settings settings)
    {
        this.client = client;
        this.settings = settings;
        Title = "Мессенджер · PixelHelper";
        Width = 1240;
        Height = 780;
        MinWidth = 840;
        MinHeight = 520;
        WindowStyle = WindowStyle.None;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(16)
        });
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = false;
        Content = root;
        ApplyTheme();

        client.Changed += OnChanged;
        client.MessageReceived += OnMessage;
        client.TypingReceived += OnTyping;
        Loaded += async (_, _) => await RefreshAsync();
        Activated += async (_, _) => await MarkReadAsync();
        SizeChanged += (_, _) => AdjustDrawer();
        Closed += (_, _) =>
        {
            closing = true;
            client.Changed -= OnChanged;
            client.MessageReceived -= OnMessage;
            client.TypingReceived -= OnTyping;
            try { if (Directory.Exists(clipboardDirectory)) Directory.Delete(clipboardDirectory, true); } catch (IOException ex) { Settings.Log(ex); }
        };
    }

    private void ApplyTheme()
    {
        bool contrast = settings.ChatTheme == "Contrast";
        Background = MessengerDesign.Brush(settings.ChatTheme, "base");
        surface = MessengerDesign.Brush(settings.ChatTheme, "surface");
        raised = MessengerDesign.Brush(settings.ChatTheme, "raised");
        ink = MessengerDesign.Brush(settings.ChatTheme, "ink");
        muted = MessengerDesign.Brush(settings.ChatTheme, "muted");
        line = MessengerDesign.Brush(settings.ChatTheme, "line");
        incoming = MessengerDesign.Brush(settings.ChatTheme, "incoming");
        accent = MessengerDesign.Brush(settings.ChatTheme, "accent");
        outgoing = contrast ? Brushes.Black : MessengerDesign.Gradient;

        Foreground = ink;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif");
        FontSize = Math.Clamp(settings.ChatFontSize, 11, 22);

        Resources["InkBrush"] = ink;
        Resources["MutedInk"] = muted;
        Resources["ContactFont"] = FontSize;
        Resources["DetailFont"] = Math.Max(11, FontSize - 1);

        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/PixelHelper;component/MessengerControls.xaml", UriKind.Relative) });
    }

    private Brush ChatBackdrop()
    {
        if (settings.ChatBackground == "Image" && File.Exists(settings.ChatBackgroundImage))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(settings.ChatBackgroundImage!, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 1600;
                bitmap.EndInit();
                bitmap.Freeze();
                return new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill, Opacity = 1 - Math.Clamp(settings.ChatBackgroundDim, 0, .85) };
            }
            catch (Exception ex) { Settings.Log(ex); }
        }
        if (settings.ChatBackground == "Dots")
        {
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(muted, null, new EllipseGeometry(new Point(2, 2), 1, 1)));
            return new DrawingBrush(group) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 24, 24), ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 24, 24), Opacity = .05 };
        }
        return Brushes.Transparent;
    }

    private List<ChatContact> contacts = [];
    private List<ChatEntry> messages = [];

    private void OnChanged() => Dispatcher.BeginInvoke(new Action(async () => { if (!closing) await RefreshAsync(); }));
    private void OnMessage(ChatEntry message) => OnChanged();

    private void OnTyping(int id, string name) => Dispatcher.BeginInvoke(new Action(async () =>
    {
        if (id != peer || chatStatus == null) return;
        chatStatus.Text = name + " печатает…";
        chatStatus.Foreground = accent;
        await Task.Delay(5000);
        if (!closing && id == peer && chatStatus != null)
        {
            var contact = contacts.FirstOrDefault(c => c.Id == peer);
            UpdateContactStatus(contact);
        }
    }));

    private Button ModernButton(string text, RoutedEventHandler click, bool primary = false, double cornerRadius = 10, Thickness? padding = null)
    {
        var button = new Button
        {
            Content = text,
            Background = primary ? outgoing : raised,
            Foreground = primary ? Brushes.White : ink,
            BorderBrush = primary ? Brushes.Transparent : line,
            BorderThickness = new Thickness(primary ? 0 : 1),
            Padding = padding ?? new Thickness(12, 6, 12, 6),
            Cursor = Cursors.Hand,
            FontSize = FontSize
        };
        button.Click += click;
        return button;
    }

    private void LoginForm()
    {
        showingLogin = true; users = null; peer = 0; contacts.Clear(); messages.Clear(); pendingId = null;
        root.Children.Clear(); root.ColumnDefinitions.Clear(); root.RowDefinitions.Clear();
        var card = new Border
        {
            Background = surface,
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Width = 420,
            Padding = new Thickness(36),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.25, Color = Colors.Black }
        };
        var panel = new StackPanel();
        var logo = new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(20), Background = MessengerDesign.Gradient,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16),
            Child = new TextBlock { Text = "PH", FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        panel.Children.Add(logo);
        panel.Children.Add(new TextBlock { Text = "PixelHelper Мессенджер", FontSize = 22, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(new TextBlock { Text = Environment.UserDomainName + "\\" + Environment.UserName, FontSize = 13, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 20) });
        error = new TextBlock { Foreground = muted, TextWrapping = TextWrapping.Wrap, Text = client.SignInStatus, Margin = new Thickness(0, 0, 0, 14), TextAlignment = TextAlignment.Center };
        panel.Children.Add(error);
        var submit = ModernButton("Войти под пользователем Windows", async (sender, _) =>
        {
            ((Button)sender).IsEnabled = false;
            try { await client.SignInWindowsAsync(true); await RefreshAsync(); }
            finally { ((Button)sender).IsEnabled = true; }
        }, primary: true, padding: new Thickness(16, 10, 16, 10));
        submit.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.Children.Add(submit);
        panel.Children.Add(new TextBlock { Text = "Вход выполняется автоматически по корпоративной учётной записи.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0), Foreground = muted, FontSize = 11, TextAlignment = TextAlignment.Center });
        card.Child = panel;
        root.Children.Add(card);
    }

    private void MainForm()
    {
        showingLogin = false;
        root.Children.Clear(); root.ColumnDefinitions.Clear(); root.RowDefinitions.Clear();
        dockNavButtons.Clear(); filterChipButtons.Clear();

        root.ColumnDefinitions.Add(new() { Width = new GridLength(68) });  // 0: Dock
        root.ColumnDefinitions.Add(new() { Width = new GridLength(336) }); // 1: Contacts list
        root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); // 2: Chat
        root.ColumnDefinitions.Add(new() { Width = new GridLength(detailsOpen ? 320 : 0) }); // 3: Drawer

        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); // 0: Top titlebar
        root.RowDefinitions.Add(new()); // 1: Content
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); // 2: Composer
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); // 3: Status / error

        BuildTopTitleBar();
        BuildLeftDock();
        BuildContactsColumn();
        BuildChatColumn();
        BuildDrawer();
        AdjustDrawer();
    }

    private void BuildTopTitleBar()
    {
        var header = new DockPanel { Background = surface, Margin = new Thickness(0, 0, 0, 0), MinHeight = 44 };
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        };

        // Window Caption buttons on right
        var caption = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button MakeCaptionBtn(string symbol, RoutedEventHandler onClick, bool isClose = false)
        {
            var btn = new Button
            {
                Content = symbol, Width = 44, Height = 40, FontSize = 13,
                Foreground = muted, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            btn.Click += onClick;
            btn.MouseEnter += (_, _) => { btn.Background = isClose ? (Brush)new BrushConverter().ConvertFromString("#DC2626")! : (Brush)new BrushConverter().ConvertFromString("#18818CF8")!; btn.Foreground = Brushes.White; };
            btn.MouseLeave += (_, _) => { btn.Background = Brushes.Transparent; btn.Foreground = muted; };
            return btn;
        }
        caption.Children.Add(MakeCaptionBtn("—", (_, _) => WindowState = WindowState.Minimized));
        caption.Children.Add(MakeCaptionBtn("□", (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
        caption.Children.Add(MakeCaptionBtn("×", (_, _) => Close(), isClose: true));
        DockPanel.SetDock(caption, Dock.Right);
        header.Children.Add(caption);

        // Header actions menu & title
        var actionsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var menuBtn = new Button
        {
            Content = "☰", Width = 36, Height = 32, FontSize = 16, Foreground = muted,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
            Margin = new Thickness(14, 0, 8, 0)
        };
        var actionsMenu = new ContextMenu { Background = surface, Foreground = ink, BorderBrush = line };
        var prefItem = new MenuItem { Header = "Оформление и уведомления…" };
        prefItem.Click += (_, _) => { new MessengerPreferences(settings) { Owner = this }.ShowDialog(); ApplyTheme(); MainForm(); };
        actionsMenu.Items.Add(prefItem);
        var reconnectItem = new MenuItem { Header = "Обновить соединение" };
        reconnectItem.Click += async (_, _) => await client.SignInWindowsAsync(true);
        actionsMenu.Items.Add(reconnectItem);
        var newGroupItem = new MenuItem { Header = "Создать рабочую группу…" };
        newGroupItem.Click += async (_, _) => { var win = new ChatGroupWindow(client) { Owner = this }; if (win.ShowDialog() == true) { peer = win.CreatedPeer; messages.Clear(); pendingId = null; await RefreshAsync(); } };
        actionsMenu.Items.Add(newGroupItem);
        if (client.CanBroadcast)
        {
            var bcastItem = new MenuItem { Header = "Массовая рассылка…" };
            bcastItem.Click += (_, _) => new ChatBroadcastWindow(client, settings) { Owner = this }.ShowDialog();
            actionsMenu.Items.Add(bcastItem);
        }
        menuBtn.Click += (_, _) => { actionsMenu.PlacementTarget = menuBtn; actionsMenu.IsOpen = true; };
        actionsPanel.Children.Add(menuBtn);

        actionsPanel.Children.Add(new TextBlock
        {
            Text = "PixelHelper · " + client.FullName,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13.5,
            Foreground = ink,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 16, 0)
        });

        // Broadcast button
        if (client.CanBroadcast)
        {
            var bcastBtn = ModernButton("📣 Рассылка", (_, _) => new ChatBroadcastWindow(client, settings) { Owner = this }.ShowDialog(), cornerRadius: 8, padding: new Thickness(10, 4, 10, 4));
            bcastBtn.Margin = new Thickness(0, 0, 6, 0);
            actionsPanel.Children.Add(bcastBtn);
        }

        header.Children.Add(actionsPanel);
        Grid.SetColumn(header, 1);
        Grid.SetColumnSpan(header, 3);
        root.Children.Add(header);
    }

    private void BuildLeftDock()
    {
        var dock = new DockPanel { Background = surface, LastChildFill = false };
        var top = new StackPanel();

        // User Avatar
        string initials = string.Concat(client.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(n => n[0]));
        var avatarBorder = new Border
        {
            CornerRadius = new CornerRadius(21),
            Background = MessengerDesign.Gradient,
            Width = 42, Height = 42,
            Margin = new Thickness(0, 10, 0, 18),
            ToolTip = client.FullName,
            Child = new TextBlock { Text = initials, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        top.Children.Add(avatarBorder);

        // Navigation filter icons
        foreach (var (value, icon, label) in new[] { ("all", "☷", "Все чаты"), ("personal", "♙", "Личные диалоги"), ("groups", "♟", "Рабочие группы"), ("unread", "◉", "Непрочитанные"), ("favourite", "☆", "Избранное") })
        {
            var nav = new Button
            {
                Content = icon,
                Width = 44, Height = 44,
                FontSize = 19,
                Background = chatFilter == value ? (Brush)new BrushConverter().ConvertFromString("#20818CF8")! : Brushes.Transparent,
                Foreground = chatFilter == value ? accent : muted,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = label,
                Margin = new Thickness(0, 2, 0, 2),
                Tag = value
            };
            nav.Click += (_, _) => { SetFilter(value); };
            dockNavButtons.Add(nav);
            top.Children.Add(nav);
        }
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);

        // Bottom dock actions: Theme toggle and Preferences
        var bottom = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var themeBtn = new Button
        {
            Content = "◐", Width = 44, Height = 44, FontSize = 18,
            Background = Brushes.Transparent, Foreground = muted, BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, ToolTip = "Переключить тему (Светлая / Тёмная)"
        };
        themeBtn.Click += (_, _) =>
        {
            settings.ChatTheme = settings.ChatTheme == "Light" ? "Dark" : "Light";
            settings.Save();
            ApplyTheme();
            MainForm();
        };
        bottom.Children.Add(themeBtn);

        var prefBtn = new Button
        {
            Content = "⚙", Width = 44, Height = 44, FontSize = 18,
            Background = Brushes.Transparent, Foreground = muted, BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, ToolTip = "Настройки"
        };
        prefBtn.Click += (_, _) =>
        {
            new MessengerPreferences(settings) { Owner = this }.ShowDialog();
            ApplyTheme();
            MainForm();
        };
        bottom.Children.Add(prefBtn);

        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(bottom);

        Grid.SetColumn(dock, 0);
        Grid.SetRowSpan(dock, 4);
        root.Children.Add(dock);
    }

    private void BuildContactsColumn()
    {
        var colBorder = new Border
        {
            Background = surface,
            BorderBrush = line,
            BorderThickness = new Thickness(1, 1, 1, 0),
            CornerRadius = new CornerRadius(16, 0, 0, 0),
            Margin = new Thickness(0, 0, 1, 0)
        };
        var colPanel = new DockPanel();

        // Header: "Сообщения" + кнопка "＋"
        var header = new DockPanel { Margin = new Thickness(16, 14, 14, 8) };
        header.Children.Add(new TextBlock { Text = "Сообщения", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = ink, VerticalAlignment = VerticalAlignment.Center });
        var addGroupBtn = new Button
        {
            Content = "＋", FontSize = 18, Width = 32, Height = 32,
            Background = raised, Foreground = ink, BorderBrush = line, BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand, ToolTip = "Создать новую группу"
        };
        addGroupBtn.Click += async (_, _) =>
        {
            var win = new ChatGroupWindow(client) { Owner = this };
            if (win.ShowDialog() == true) { peer = win.CreatedPeer; messages.Clear(); pendingId = null; await RefreshAsync(); }
        };
        DockPanel.SetDock(addGroupBtn, Dock.Right);
        header.Children.Add(addGroupBtn);
        DockPanel.SetDock(header, Dock.Top);
        colPanel.Children.Add(header);

        // Search Bar with integrated icon & placeholder
        var searchContainer = new Border
        {
            Background = raised,
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(14, 2, 14, 8),
            Padding = new Thickness(10, 6, 10, 6)
        };
        var searchGrid = new Grid();
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var searchIcon = new TextBlock { Text = "⌕", FontSize = 16, Foreground = muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(searchIcon, 0);
        searchGrid.Children.Add(searchIcon);

        search = new TextBox
        {
            Background = Brushes.Transparent, Foreground = ink, BorderThickness = new Thickness(0),
            CaretBrush = ink, FontSize = FontSize, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0)
        };
        Grid.SetColumn(search, 1);
        searchGrid.Children.Add(search);

        var placeholder = new TextBlock
        {
            Text = "Поиск сотрудников и групп…", Foreground = muted, FontSize = FontSize - 0.5,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
        };
        Grid.SetColumn(placeholder, 1);
        searchGrid.Children.Add(placeholder);

        var clearBtn = new Button
        {
            Content = "×", FontSize = 16, Foreground = muted, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Visibility = Visibility.Collapsed,
            Padding = new Thickness(4, 0, 4, 0)
        };
        Grid.SetColumn(clearBtn, 2);
        searchGrid.Children.Add(clearBtn);

        search.TextChanged += (_, _) =>
        {
            placeholder.Visibility = string.IsNullOrEmpty(search.Text) ? Visibility.Visible : Visibility.Collapsed;
            clearBtn.Visibility = string.IsNullOrEmpty(search.Text) ? Visibility.Collapsed : Visibility.Visible;
            Filter();
        };
        clearBtn.Click += (_, _) => { search.Text = ""; search.Focus(); };
        searchContainer.Child = searchGrid;
        DockPanel.SetDock(searchContainer, Dock.Top);
        colPanel.Children.Add(searchContainer);

        // Filter chips row
        filterChipsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 14, 8) };
        void AddChip(string label, string filterVal)
        {
            var btn = new Button
            {
                Content = label,
                Padding = new Thickness(11, 4, 11, 4),
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 11.5,
                Cursor = Cursors.Hand,
                Background = chatFilter == filterVal ? (Brush)new BrushConverter().ConvertFromString("#25818CF8")! : raised,
                Foreground = chatFilter == filterVal ? accent : muted,
                BorderBrush = chatFilter == filterVal ? (Brush)new BrushConverter().ConvertFromString("#50818CF8")! : line,
                BorderThickness = new Thickness(1),
                Tag = filterVal
            };
            btn.Click += (_, _) => SetFilter(filterVal);
            filterChipButtons.Add(btn);
            filterChipsRow.Children.Add(btn);
        }
        AddChip("Все", "all");
        AddChip("Непрочитанные", "unread");
        AddChip("Работа", "groups");
        AddChip("Личные", "personal");
        DockPanel.SetDock(filterChipsRow, Dock.Top);
        colPanel.Children.Add(filterChipsRow);

        // Contacts ListBox
        users = new ListBox
        {
            Background = Brushes.Transparent,
            Foreground = ink,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(6, 0, 6, 6),
            ItemTemplate = (DataTemplate)XamlReader.Parse("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  <Grid Margin="4,2">
                    <Grid.ColumnDefinitions>
                      <ColumnDefinition Width="Auto"/>
                      <ColumnDefinition Width="*"/>
                      <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <Grid.RowDefinitions>
                      <RowDefinition Height="Auto"/>
                      <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>
                    <Grid Grid.RowSpan="2" Margin="0,0,10,0" VerticalAlignment="Center">
                      <Border Width="44" Height="44" CornerRadius="22" Background="#6366F1">
                        <TextBlock Text="{Binding Initials}" Foreground="White" HorizontalAlignment="Center" VerticalAlignment="Center" FontSize="13.5" FontWeight="SemiBold"/>
                      </Border>
                      <Ellipse Width="11" Height="11" HorizontalAlignment="Right" VerticalAlignment="Bottom" Stroke="#101521" StrokeThickness="2" Margin="0,0,0,1">
                        <Ellipse.Style>
                          <Style TargetType="Ellipse">
                            <Setter Property="Fill" Value="#64748B"/>
                            <Style.Triggers>
                              <DataTrigger Binding="{Binding IsOnline}" Value="True">
                                <Setter Property="Fill" Value="#10B981"/>
                              </DataTrigger>
                              <DataTrigger Binding="{Binding IsGroup}" Value="True">
                                <Setter Property="Visibility" Value="Collapsed"/>
                              </DataTrigger>
                            </Style.Triggers>
                          </Style>
                        </Ellipse.Style>
                      </Ellipse>
                    </Grid>
                    <TextBlock Grid.Column="1" Text="{Binding Label}" ToolTip="{Binding FullName}" TextTrimming="CharacterEllipsis" FontSize="{DynamicResource ContactFont}" FontWeight="Medium" Foreground="{DynamicResource InkBrush}" VerticalAlignment="Center"/>
                    <TextBlock Grid.Column="2" Text="{Binding TimeLabel}" Foreground="{DynamicResource MutedInk}" FontSize="11" Margin="6,0,0,0" VerticalAlignment="Center"/>
                    <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding Subtitle}" TextTrimming="CharacterEllipsis" Foreground="{DynamicResource MutedInk}" FontSize="{DynamicResource DetailFont}" Margin="0,3,6,0"/>
                    <Border Grid.Row="1" Grid.Column="2" Background="#818CF8" CornerRadius="9" Padding="6,1" VerticalAlignment="Center" Margin="0,2,0,0">
                      <Border.Style>
                        <Style TargetType="Border">
                          <Setter Property="Visibility" Value="Collapsed"/>
                          <Style.Triggers>
                            <DataTrigger Binding="{Binding HasUnread}" Value="True">
                              <Setter Property="Visibility" Value="Visible"/>
                            </DataTrigger>
                          </Style.Triggers>
                        </Style>
                      </Border.Style>
                      <TextBlock Text="{Binding Unread}" Foreground="White" FontWeight="Bold" FontSize="10.5"/>
                    </Border>
                  </Grid>
                </DataTemplate>
                """)
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(users, ScrollBarVisibility.Disabled);
        users.SelectionChanged += async (_, _) =>
        {
            if (updating || users.SelectedItem is not ChatContact contact) return;
            if (peer != contact.Id) { peer = contact.Id; input!.Text = ""; pendingId = null; pendingFiles.Clear(); RenderPendingFiles(); messages.Clear(); }
            await LoadHistoryAsync();
        };
        users.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject src && ItemsControl.ContainerFromElement(users, src) is ListBoxItem item) users.SelectedItem = item.DataContext;
        };
        SetupContactContextMenu();

        colPanel.Children.Add(users);
        colBorder.Child = colPanel;
        Grid.SetColumn(colBorder, 1);
        Grid.SetRow(colBorder, 1);
        Grid.SetRowSpan(colBorder, 2);
        root.Children.Add(colBorder);
    }

    private void SetupContactContextMenu()
    {
        var contactMenu = new ContextMenu { Background = surface, Foreground = ink, BorderBrush = line };
        var invite = new MenuItem { Header = "Добавить в группу…" };
        contactMenu.Items.Add(invite);
        var groupSettings = new MenuItem { Header = "Настройки группы…" };
        groupSettings.Click += async (_, _) => { if (users?.SelectedItem is not ChatContact { IsGroup: true } group) return; new ChatGroupWindow(client, group.Id, settings) { Owner = this }.ShowDialog(); await RefreshAsync(); };
        contactMenu.Items.Add(groupSettings);
        foreach (var (label, operation) in new[] { ("Выйти из группы…", "leave"), ("Удалить группу…", "delete") })
        {
            var item = new MenuItem { Header = label };
            contactMenu.Items.Add(item);
            contactMenu.Opened += (_, _) => item.Visibility = users?.SelectedItem is ChatContact { IsGroup: true } c && (operation == "leave" || c.OwnerId == client.UserId) ? Visibility.Visible : Visibility.Collapsed;
            item.Click += async (_, _) =>
            {
                if (users?.SelectedItem is not ChatContact { IsGroup: true } group) return;
                if (MessageBox.Show(this, operation == "delete" ? "Удалить «" + group.FullName + "» для всех участников? История сохранится на сервере." : "Выйти из «" + group.FullName + "»?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                try { await client.GroupOperationAsync(group.Id, operation); await RefreshAsync(); } catch (Exception ex) { error!.Text = ex.Message; }
            };
        }
        foreach (var (label, kind) in new[] { ("Закрепить / открепить", "pin"), ("Избранное / убрать", "favourite"), ("Без звука / включить", "mute") })
        {
            var item = new MenuItem { Header = label };
            item.Click += async (_, _) =>
            {
                if (users?.SelectedItem is not ChatContact c) return;
                try { await client.PreferenceAsync(c.Id, kind == "pin" ? !c.Pinned : c.Pinned, kind == "favourite" ? !c.Favourite : c.Favourite, kind == "mute" ? !c.Muted : c.Muted); await RefreshAsync(); } catch (Exception ex) { error!.Text = ex.Message; }
            };
            contactMenu.Items.Add(item);
        }
        contactMenu.Opened += async (_, _) =>
        {
            groupSettings.Visibility = users?.SelectedItem is ChatContact { IsGroup: true } ? Visibility.Visible : Visibility.Collapsed;
            invite.Items.Clear();
            invite.IsEnabled = users?.SelectedItem is ChatContact { IsGroup: false, IsActive: true };
            if (users?.SelectedItem is not ChatContact selected || !invite.IsEnabled) return;
            foreach (var group in contacts.Where(c => c.IsGroup && c.IsActive && (c.OwnerId == client.UserId || c.IsAdmin)))
            {
                try
                {
                    var info = await client.GroupAsync(group.Id);
                    if (info.Members.Any(m => m.Id == selected.Id)) continue;
                    var item = new MenuItem { Header = group.FullName };
                    item.Click += async (_, _) =>
                    {
                        if (MessageBox.Show(this, "Добавить " + selected.FullName + " в «" + group.FullName + "»?", Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                        try { await client.GroupOperationAsync(group.Id, "invite", new { userId = selected.Id }); await RefreshAsync(); } catch (Exception ex) { error!.Text = ex.Message; }
                    };
                    invite.Items.Add(item);
                }
                catch (Exception ex) { error!.Text = ex.Message; }
            }
        };
        users!.ContextMenu = contactMenu;
    }

    private void SetFilter(string filterValue)
    {
        chatFilter = filterValue;
        foreach (var btn in dockNavButtons)
        {
            bool active = (string)btn.Tag == filterValue;
            btn.Background = active ? (Brush)new BrushConverter().ConvertFromString("#20818CF8")! : Brushes.Transparent;
            btn.Foreground = active ? accent : muted;
        }
        foreach (var chip in filterChipButtons)
        {
            bool active = (string)chip.Tag == filterValue;
            chip.Background = active ? (Brush)new BrushConverter().ConvertFromString("#25818CF8")! : raised;
            chip.Foreground = active ? accent : muted;
            chip.BorderBrush = active ? (Brush)new BrushConverter().ConvertFromString("#50818CF8")! : line;
        }
        Filter();
    }

    private void BuildChatColumn()
    {
        var chatPanel = new DockPanel { Background = Brushes.Transparent };

        // 1. Chat Header
        var headerBorder = new Border
        {
            Background = surface,
            BorderBrush = line,
            BorderThickness = new Thickness(0, 1, 0, 1),
            MinHeight = 56,
            Padding = new Thickness(16, 8, 16, 8)
        };
        var headerDock = new DockPanel();

        // Right side header buttons: Search & Info
        var headerRight = new StackPanel { Orientation = Orientation.Horizontal };
        var searchInChatBtn = ModernButton("⌕", (_, _) => SearchHistory(), cornerRadius: 8, padding: new Thickness(10, 6, 10, 6));
        searchInChatBtn.ToolTip = "Поиск по переписке";
        searchInChatBtn.Margin = new Thickness(0, 0, 6, 0);
        headerRight.Children.Add(searchInChatBtn);

        var infoBtn = ModernButton("ⓘ", async (_, _) =>
        {
            detailsOpen = !detailsOpen;
            AdjustDrawer();
            await LoadDetailsAsync();
            root.UpdateLayout();
            RenderHistory(false);
        }, cornerRadius: 8, padding: new Thickness(10, 6, 10, 6));
        infoBtn.ToolTip = "Информация о собеседнике / группе";
        headerRight.Children.Add(infoBtn);
        DockPanel.SetDock(headerRight, Dock.Right);
        headerDock.Children.Add(headerRight);

        // Left side: Avatar + Identity
        var headerLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        chatAvatar = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(20),
            Background = MessengerDesign.Gradient, Margin = new Thickness(0, 0, 12, 0),
            Child = new TextBlock { Text = "PH", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 13.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        headerLeft.Children.Add(chatAvatar);

        var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title = new TextBlock { Text = "Выберите сотрудника", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = ink };
        identity.Children.Add(title);
        chatStatus = new TextBlock { Text = "Сообщения, файлы и рабочие группы", FontSize = 11.5, Foreground = muted, Margin = new Thickness(0, 2, 0, 0) };
        identity.Children.Add(chatStatus);
        headerLeft.Children.Add(identity);

        headerDock.Children.Add(headerLeft);
        headerBorder.Child = headerDock;
        DockPanel.SetDock(headerBorder, Dock.Top);
        chatPanel.Children.Add(headerBorder);

        // 2. Chat history scroll
        history = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        scroll = new ScrollViewer
        {
            ClipToBounds = true,
            Content = history,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = ChatBackdrop(),
            Padding = new Thickness(0)
        };
        chatPanel.Children.Add(scroll);

        Grid.SetColumn(chatPanel, 2);
        Grid.SetRow(chatPanel, 1);
        root.Children.Add(chatPanel);

        // 3. Unified Composer
        BuildComposer();
    }

    private void BuildComposer()
    {
        var composerOuter = new StackPanel { Background = Brushes.Transparent };

        // Attachments preview
        attachmentsPanel = new WrapPanel { Margin = new Thickness(16, 0, 16, 4) };
        var attachmentsScroll = new ScrollViewer { Content = attachmentsPanel, MaxHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        composerOuter.Children.Add(attachmentsScroll);

        // Unified Composer Card
        var composerCard = new Border
        {
            Background = surface,
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(14, 4, 14, 10),
            Padding = new Thickness(6, 4, 8, 4),
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.12, Color = Colors.Black }
        };
        var composerDock = new DockPanel();

        // Send Button
        send = new Button
        {
            Content = "➤",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Background = MessengerDesign.Gradient,
            BorderThickness = new Thickness(0),
            Width = 38, Height = 36,
            Cursor = Cursors.Hand,
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "Отправить (Enter)"
        };
        send.Click += async (_, _) => await SendAsync();
        DockPanel.SetDock(send, Dock.Right);
        composerDock.Children.Add(send);

        // Emoji Button
        var emojiBtn = new Button
        {
            Content = "☺",
            FontSize = 19,
            Foreground = muted,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Width = 34, Height = 36,
            Cursor = Cursors.Hand,
            ToolTip = "Эмодзи и стикеры"
        };
        emojiBtn.Click += (_, _) => ShowEmojiPicker(emojiBtn);
        DockPanel.SetDock(emojiBtn, Dock.Right);
        composerDock.Children.Add(emojiBtn);

        // File Attach Button
        var fileBtn = new Button
        {
            Content = "📎",
            FontSize = 17,
            Foreground = muted,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Width = 34, Height = 36,
            Cursor = Cursors.Hand,
            Margin = new Thickness(2, 0, 4, 0),
            ToolTip = "Прикрепить файлы (до 50 МБ)"
        };
        fileBtn.Click += (_, _) => ChooseFiles();
        DockPanel.SetDock(fileBtn, Dock.Left);
        composerDock.Children.Add(fileBtn);

        // Input TextBox with Placeholder
        var inputGrid = new Grid();
        input = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxLength = 4000,
            MinHeight = 36,
            MaxHeight = 130,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = ink,
            CaretBrush = ink,
            FontSize = FontSize,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 6, 4, 6)
        };
        var inputPlaceholder = new TextBlock
        {
            Text = "Написать сообщение…",
            Foreground = muted,
            FontSize = FontSize,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            IsHitTestVisible = false
        };
        input.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
            {
                e.Handled = true;
                await SendAsync();
            }
        };
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && !sending && input.IsEnabled)
            {
                try
                {
                    if (Clipboard.ContainsImage()) { e.Handled = true; PasteImage(); }
                    else if (Clipboard.ContainsFileDropList()) { e.Handled = true; AddFiles(Clipboard.GetFileDropList().Cast<string>()); }
                }
                catch (Exception ex) { error!.Text = "Не удалось вставить вложение: " + ex.Message; }
            }
        };
        input.TextChanged += async (_, _) =>
        {
            inputPlaceholder.Visibility = string.IsNullOrEmpty(input.Text) ? Visibility.Visible : Visibility.Collapsed;
            if (peer != 0 && input.Text.Length > 0 && DateTime.UtcNow - typingSent > TimeSpan.FromSeconds(2))
            {
                typingSent = DateTime.UtcNow;
                await client.TypingAsync(peer);
            }
        };
        inputGrid.Children.Add(inputPlaceholder);
        inputGrid.Children.Add(input);

        composerDock.Children.Add(inputGrid);
        composerCard.Child = composerDock;
        composerOuter.Children.Add(composerCard);

        // Error line
        error = new TextBlock { Foreground = (Brush)new BrushConverter().ConvertFromString("#F43F5E")!, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 0, 16, 4), FontSize = 12 };
        composerOuter.Children.Add(error);

        Grid.SetColumn(composerOuter, 2);
        Grid.SetRow(composerOuter, 2);
        root.Children.Add(composerOuter);
    }

    private void BuildDrawer()
    {
        details = new StackPanel { Margin = new Thickness(16, 14, 14, 14) };
        var detailsScroll = new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = surface };
        detailsView = detailsScroll;
        Grid.SetColumn(detailsScroll, 3);
        Grid.SetRow(detailsScroll, 1);
        Grid.SetRowSpan(detailsScroll, 2);
        root.Children.Add(detailsScroll);
    }

    private void AdjustDrawer()
    {
        if (root.ColumnDefinitions.Count != 4 || detailsView == null) return;
        bool overlay = ActualWidth > 0 && ActualWidth < 1100;
        root.ColumnDefinitions[3].Width = new GridLength(detailsOpen && !overlay ? 320 : 0);
        detailsView.Visibility = detailsOpen ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(detailsView, overlay ? 2 : 3);
        Grid.SetColumnSpan(detailsView, overlay ? 2 : 1);
        detailsView.Width = overlay ? 320 : double.NaN;
        detailsView.HorizontalAlignment = overlay ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        Panel.SetZIndex(detailsView, 2);
    }

    private void Filter()
    {
        if (users == null) return;
        bool wasUpdating = updating;
        updating = true;
        string term = search?.Text.Trim() ?? "";
        users.ItemsSource = ChatContact.Ordered(contacts.Where(u =>
            (chatFilter == "all" ||
             chatFilter == "favourite" && u.Favourite ||
             chatFilter == "unread" && u.Unread > 0 ||
             chatFilter == "groups" && u.IsGroup ||
             chatFilter == "personal" && !u.IsGroup) &&
            (u.FullName + " " + u.Username + " " + u.Branch).Contains(term, StringComparison.OrdinalIgnoreCase))).ToList();
        users.SelectedItem = contacts.FirstOrDefault(u => u.Id == peer);
        updating = wasUpdating;
    }

    private async Task RefreshAsync()
    {
        if (closing || updating) return;
        if (!client.SignedIn) { if (!showingLogin) LoginForm(); else if (error != null) error.Text = client.SignInStatus; return; }
        if (users == null || showingLogin) MainForm();
        updating = true;
        try
        {
            contacts = await client.UsersAsync();
            if (peer < 0 && !contacts.Any(c => c.Id == peer))
            {
                peer = 0;
                messages.Clear();
                history?.Children.Clear();
                if (title != null) title.Text = "Группа недоступна";
                if (input != null) input.Clear();
                pendingId = null;
            }
            Filter();
            if (peer != 0) await LoadHistoryAsync();
            else UpdateContactStatus(null);
        }
        catch (Exception ex) { if (error != null) error.Text = ex.Message; }
        finally { updating = false; }
    }

    internal async Task OpenPeerAsync(int id)
    {
        peer = id;
        await RefreshAsync();
        await LoadHistoryAsync();
        Activate();
    }

    private void UpdateContactStatus(ChatContact? contact)
    {
        if (title == null || chatStatus == null || chatAvatar == null) return;
        if (contact == null)
        {
            title.Text = "Выберите сотрудника";
            chatStatus.Text = "Сообщения, файлы и рабочие группы";
            chatStatus.Foreground = muted;
            if (chatAvatar.Child is TextBlock tb) tb.Text = "PH";
            return;
        }

        title.Text = contact.FullName;
        if (chatAvatar.Child is TextBlock avatarText) avatarText.Text = contact.Initials;
        if (contact.IsGroup)
        {
            chatStatus.Text = "Рабочая группа";
            chatStatus.Foreground = muted;
        }
        else if (contact.IsOnline)
        {
            chatStatus.Text = "● В сети";
            chatStatus.Foreground = (Brush)new BrushConverter().ConvertFromString("#10B981")!;
        }
        else
        {
            chatStatus.Text = "○ Не в сети";
            chatStatus.Foreground = muted;
        }
    }

    private async Task LoadHistoryAsync()
    {
        if (peer == 0 || !client.SignedIn || closing) return;
        int selected = peer;
        var selectedContact = contacts.FirstOrDefault(c => c.Id == selected);
        UpdateContactStatus(selectedContact);

        if (selectedContact?.SuspendedUntil > DateTime.UtcNow)
        {
            messages.Clear();
            RenderHistory(false);
            if (input != null) input.IsEnabled = false;
            if (send != null) send.IsEnabled = false;
            if (error != null) error.Text = "Доступ к группе отключён до " + selectedContact.SuspendedUntil.Value.ToLocalTime().ToString("dd.MM HH:mm");
            return;
        }

        try
        {
            var latest = await client.HistoryAsync(selected);
            if (selected != peer || closing) return;
            var events = selected < 0 ? (await client.GroupAsync(selected)).Events ?? [] : [];
            if (selected != peer || closing) return;
            groupEvents = events;
            eventsPeer = selected;
            reactions = await client.ReactionsAsync(selected, latest.Select(m => m.Id));
            if (selected != peer || closing) return;

            messages = messages.Where(m => selected < 0 ? m.RecipientId == selected : m.SenderId == selected || m.RecipientId == selected)
                .Concat(latest).GroupBy(m => m.Id).Select(g => g.Last()).OrderBy(m => m.Id).ToList();

            bool writable = selectedContact?.IsActive == true;
            if (input != null) input.IsEnabled = writable && !sending;
            if (send != null) send.IsEnabled = writable && !sending;

            RenderHistory(scroll!.ScrollableHeight - scroll.VerticalOffset < 5);
            await MarkReadAsync();
            if (error != null) error.Text = "";
            await LoadDetailsAsync();
        }
        catch (Exception ex) { if (error != null) error.Text = ex.Message; }
    }

    private void RenderHistory(bool bottom)
    {
        if (history == null) return;
        history.Children.Clear();

        if (peer == 0)
        {
            // Empty state placeholder
            var welcome = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 80, 0, 0)
            };
            var logo = new Border
            {
                Width = 64, Height = 64, CornerRadius = new CornerRadius(20),
                Background = MessengerDesign.Gradient, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16),
                Child = new TextBlock { Text = "PH", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            welcome.Children.Add(logo);
            welcome.Children.Add(new TextBlock { Text = "PixelHelper Мессенджер", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) });
            welcome.Children.Add(new TextBlock { Text = "Выберите сотрудника или рабочую группу в списке слева для начала общения", FontSize = 13, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 360, TextAlignment = TextAlignment.Center });
            history.Children.Add(welcome);
            return;
        }

        // Show "Load previous messages" pill button if there's enough history
        if (messages.Count >= 50)
        {
            var older = new Button
            {
                Content = "Предыдущие сообщения",
                Background = raised,
                Foreground = muted,
                BorderBrush = line,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 5, 16, 5),
                FontSize = 11.5,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 10)
            };
            older.Click += async (_, _) =>
            {
                if (peer == 0 || messages.Count == 0) return;
                try
                {
                    var previous = await client.HistoryAsync(peer, messages.Min(m => m.Id));
                    messages = previous.Concat(messages).DistinctBy(m => m.Id).OrderBy(m => m.Id).ToList();
                    RenderHistory(false);
                }
                catch (Exception ex) { if (error != null) error.Text = ex.Message; }
            };
            history.Children.Add(older);
        }

        if (messages.Count == 0)
        {
            history.Children.Add(new TextBlock
            {
                Text = "В этом диалоге ещё нет сообщений. Напишите первым!",
                Foreground = muted,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0)
            });
            return;
        }

        DateTime? date = null;
        var systemEntries = eventsPeer == peer && peer < 0
            ? groupEvents.Where(e => messages.Count == 0 || e.SentAt >= messages.Min(m => m.SentAt)).Select(e => new ChatEntry(-1, 0, peer, e.Body, e.Id, e.SentAt, null, IsSystem: true))
            : [];

        foreach (var message in messages.Concat(systemEntries).Concat(pendingMessage != null && pendingMessage.RecipientId == peer ? new[] { pendingMessage } : Array.Empty<ChatEntry>())
            .Select(m => m with { Body = System.Text.RegularExpressions.Regex.Replace(m.Body, @"^(?:(?:/срочно|/танец)(?:\s+|$))+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase) })
            .OrderBy(m => m.SentAt))
        {
            if (string.IsNullOrWhiteSpace(message.Body) && message.Attachments?.Count is not > 0) continue;
            var day = message.SentAt.ToLocalTime().Date;
            if (date != day)
            {
                var datePill = new Border
                {
                    Background = raised,
                    BorderBrush = line,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(999),
                    Padding = new Thickness(14, 3, 14, 3),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 12, 0, 12),
                    Child = new TextBlock { Text = day == DateTime.Today ? "Сегодня" : day.ToString("d MMMM"), Foreground = muted, FontSize = 11 }
                };
                history.Children.Add(datePill);
                date = day;
            }

            if (message.IsSystem)
            {
                history.Children.Add(new TextBlock
                {
                    Text = message.SentAt.ToLocalTime().ToString("HH:mm") + " · " + message.Body,
                    Foreground = muted,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(12, 6, 12, 6),
                    FontSize = 11.5,
                    MaxWidth = 600
                });
                continue;
            }

            bool mine = message.SenderId == client.UserId;
            var panel = new StackPanel();

            if (message.Id == 0)
            {
                panel.Children.Add(new TextBlock { Text = message.Body, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White });
                panel.Children.Add(new TextBlock { Text = pendingFailed ? "Ошибка · повторите отправку" : "Отправляется…", FontSize = 11, Foreground = Brushes.White, Margin = new Thickness(0, 4, 0, 0) });
                history.Children.Add(new Border
                {
                    Child = panel,
                    Background = outgoing,
                    CornerRadius = new CornerRadius(16, 16, 4, 16),
                    Padding = new Thickness(14, 10, 14, 10),
                    Margin = new Thickness(4),
                    MaxWidth = 520,
                    HorizontalAlignment = HorizontalAlignment.Right
                });
                continue;
            }

            // Context menu for reactions
            var reactionMenu = new ContextMenu { Background = surface, Foreground = ink, BorderBrush = line };
            foreach (string emoji in new[] { "👍", "❤️", "😊", "🎉", "😔", "👋" })
            {
                var item = new MenuItem { Header = emoji, FontSize = 16 };
                item.Click += async (_, _) =>
                {
                    try { await client.ReactAsync(peer, message.Id, emoji); await LoadHistoryAsync(); } catch (Exception ex) { error!.Text = ex.Message; }
                };
                reactionMenu.Items.Add(item);
            }
            panel.ContextMenu = reactionMenu;

            // Sender name in group chats
            if (peer < 0 && !mine)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = message.SenderName ?? "Участник",
                    FontWeight = FontWeights.Bold,
                    Foreground = accent,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 4)
                });
            }

            if (message.IsUrgent)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "⚠ СРОЧНО",
                    Foreground = (Brush)new BrushConverter().ConvertFromString("#F59E0B")!,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 6)
                });
            }

            // Body text with emoji render
            var body = HelperEmojis.Render(message.Body, Math.Max(32, FontSize * 2.3), settings.AnimatedChatEmojis);
            body.FontSize = FontSize;
            body.Foreground = mine ? Brushes.White : ink;
            panel.Children.Add(body);

            // Attachments
            foreach (var attachment in message.Attachments ?? [])
            {
                var file = ModernButton(attachment.Label, async (_, _) => await OpenFileAsync(attachment), cornerRadius: 8, padding: new Thickness(10, 6, 10, 6));
                file.Margin = new Thickness(0, 6, 0, 2);
                file.ToolTip = attachment.IsImage ? "Просмотр изображения и скачивание" : (attachment.IsTextDocument || attachment.IsPdf ? "Предпросмотр документа и скачивание" : "Скачать файл");
                panel.Children.Add(file);
            }

            if (message.IsUrgent && message.RecipientId == client.UserId && message.AcknowledgedAt == null)
            {
                panel.Children.Add(ModernButton("Ознакомился", async (_, _) =>
                {
                    try { await client.AcknowledgeAsync(message.Id); await LoadHistoryAsync(); } catch (Exception ex) { error!.Text = ex.Message; }
                }, cornerRadius: 8, padding: new Thickness(10, 4, 10, 4)));
            }

            if (message.AcknowledgedAt != null)
            {
                panel.Children.Add(new TextBlock { Text = "✓ Ознакомление подтверждено", Foreground = accent, FontSize = Math.Max(11, FontSize - 2), Margin = new Thickness(0, 4, 0, 0) });
            }

            // Timestamp and read receipt
            string timeStr = message.SentAt.ToLocalTime().ToString("HH:mm");
            string statusIcon = mine ? (message.ReadAt != null ? " ✓✓" : " ✓") : "";
            panel.Children.Add(new TextBlock
            {
                Text = timeStr + statusIcon,
                FontSize = 10.5,
                Foreground = mine ? (Brush)new BrushConverter().ConvertFromString("#E0E7FF")! : muted,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 5, 0, 0)
            });

            // Reaction chips
            var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var reaction in reactions.GetValueOrDefault(message.Id.ToString()) ?? [])
            {
                var chip = ModernButton(reaction.Emoji + " " + reaction.Count, async (_, _) =>
                {
                    try { await client.ReactAsync(peer, message.Id, reaction.Emoji); await LoadHistoryAsync(); } catch (Exception ex) { error!.Text = ex.Message; }
                }, cornerRadius: 999, padding: new Thickness(8, 2, 8, 2));
                chip.FontSize = 11;
                chip.Margin = new Thickness(0, 0, 4, 0);
                chips.Children.Add(chip);
            }
            if (chips.Children.Count > 0) panel.Children.Add(chips);

            var bubble = new Border
            {
                Child = panel,
                Background = mine ? outgoing : incoming,
                BorderBrush = mine ? Brushes.Transparent : line,
                BorderThickness = new Thickness(mine ? 0 : 1),
                CornerRadius = mine ? new CornerRadius(16, 16, 4, 16) : new CornerRadius(16, 16, 16, 4),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness( mine ? 48 : 0, 3, mine ? 0 : 48, 3),
                MaxWidth = Math.Max(260, (scroll is { ActualWidth: > 0 } ? scroll.ActualWidth : ActualWidth - 420) * .78),
                HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.08, Color = Colors.Black }
            };
            history.Children.Add(bubble);
        }

        if (bottom) scroll!.ScrollToEnd();
    }

    private void ShowEmojiPicker(Button target)
    {
        if (input?.IsEnabled != true) return;
        if (emojiPopup != null && emojiPopup.IsOpen) { emojiPopup.IsOpen = false; return; }

        emojiPopup = new Popup
        {
            PlacementTarget = target,
            Placement = PlacementMode.Top,
            StaysOpen = false,
            AllowsTransparency = true
        };

        var card = new Border
        {
            Background = surface,
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(12),
            Width = 360,
            Height = 320,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.25, Color = Colors.Black }
        };

        var dock = new DockPanel();
        var tabsHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var emojiContent = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        void SwitchTab(string category, string[] emojis)
        {
            var wrap = new WrapPanel { Margin = new Thickness(2) };
            if (category == "Помощник")
            {
                foreach (var emoji in HelperEmojis.All)
                {
                    var btn = new Button
                    {
                        Width = 100, Height = 48, Margin = new Thickness(3),
                        Background = raised, BorderBrush = line, BorderThickness = new Thickness(1),
                        Cursor = Cursors.Hand
                    };
                    var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                    p.Children.Add(HelperEmojis.AnimatedImage(emoji, 28, settings.AnimatedChatEmojis));
                    p.Children.Add(new TextBlock { Text = emoji.Name, FontSize = 11, Foreground = ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
                    btn.Content = p;
                    btn.Click += (_, _) => { InsertEmoji(emoji); emojiPopup.IsOpen = false; };
                    wrap.Children.Add(btn);
                }
            }
            else
            {
                foreach (string em in emojis)
                {
                    var btn = new Button
                    {
                        Content = em, FontSize = 18, Width = 36, Height = 36, Margin = new Thickness(2),
                        Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand
                    };
                    btn.Click += (_, _) =>
                    {
                        if (input != null && input.IsEnabled)
                        {
                            int idx = input.SelectionStart;
                            input.SelectedText = em;
                            input.CaretIndex = idx + em.Length;
                            input.Focus();
                        }
                        emojiPopup.IsOpen = false;
                    };
                    wrap.Children.Add(btn);
                }
            }
            emojiContent.Content = wrap;
        }

        var tabCategories = new[]
        {
            ("Помощник", Array.Empty<string>()),
            ("Смайлы", new[] { "😀","😁","😂","🤣","😃","😄","😅","😆","😉","😊","😋","😎","😍","😘","😗","😙","😚","☺️","🙂","🤗","🤩","🤔","🤨","😐","😑","😶","🙄","😏","😣","😥","😮","🤐","😯","😪","😫","😴","😌","😛","😜","🤪","😝","🤤","😒","😓","😔","😕","🙃","🤑","😲","🙁","😖","😞","😟","😤","😢","😭","😦","😧","😨","😩","🤯","😬","😰","😱","🥵","🥶","😳","😵","😡","😠","🤬","😷","🤒","🤕","🤢","🤧","😇","🤠","🥳","🥺" }),
            ("Жесты", new[] { "👍","👎","👏","🙌","🤝","👊","✊","🤛","🤜","🤞","✌️","🤟","🤘","👌","🤏","👈","👉","👆","👇","☝️","✋","🤚","🖐️","🖖","👋","🤙","💪","🙏","✍️" }),
            ("Сердца", new[] { "❤️","🧡","💛","💚","💙","💜","🖤","🤍","🤎","💔","❣️","💕","💞","💓","💗","💖","💘","💝","💟" }),
            ("Работа", new[] { "💻","🖥️","🖨️","📱","📞","✉️","📧","📨","📩","📦","📁","📂","📄","📃","📊","📈","📉","📋","📌","📍","📎","⏰","⏳","💡","🔍","🔎","🔒","🔑","🛠️","⚙️","☕","🚀","🎯","🏆","📅","🏢" })
        };

        foreach (var (tabName, list) in tabCategories)
        {
            var tBtn = new Button
            {
                Content = tabName, FontSize = 11, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 4, 0),
                Background = raised, Foreground = muted, BorderBrush = line, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            tBtn.Click += (_, _) =>
            {
                foreach (Button b in tabsHeader.Children) { b.Background = raised; b.Foreground = muted; }
                tBtn.Background = (Brush)new BrushConverter().ConvertFromString("#25818CF8")!;
                tBtn.Foreground = accent;
                SwitchTab(tabName, list);
            };
            tabsHeader.Children.Add(tBtn);
        }

        // Default to Helper tab
        if (tabsHeader.Children.Count > 0 && tabsHeader.Children[0] is Button firstBtn)
        {
            firstBtn.Background = (Brush)new BrushConverter().ConvertFromString("#25818CF8")!;
            firstBtn.Foreground = accent;
            SwitchTab(tabCategories[0].Item1, tabCategories[0].Item2);
        }

        DockPanel.SetDock(tabsHeader, Dock.Top);
        dock.Children.Add(tabsHeader);
        dock.Children.Add(emojiContent);
        card.Child = dock;
        emojiPopup.Child = card;
        emojiPopup.IsOpen = true;
    }

    internal void InsertEmoji(HelperEmoji emoji)
    {
        if (input == null || !input.IsEnabled) return;
        if (input.Text.Length - input.SelectionLength + emoji.Code.Length > 4000) { if (error != null) error.Text = "Сообщение не должно превышать 4000 символов."; return; }
        int index = input.SelectionStart;
        input.SelectedText = emoji.Code;
        input.CaretIndex = index + emoji.Code.Length;
        input.Focus();
        EmojiInserted?.Invoke(emoji);
    }

    private async Task MarkReadAsync()
    {
        if (!IsActive || peer == 0 || messages.Count == 0 || !client.SignedIn || scroll?.ScrollableHeight - scroll?.VerticalOffset > 100) return;
        var unread = messages.Where(m => (peer < 0 ? m.SenderId != client.UserId : m.RecipientId == client.UserId) && m.ReadAt == null).ToList();
        if (unread.Count == 0) return;
        int selected = peer;
        long through = unread.Max(m => m.Id);
        try
        {
            await client.ReadAsync(selected, through);
            contacts = contacts.Select(c => c.Id == selected && (c.LastId ?? 0) <= through ? c with { Unread = 0 } : c).ToList();
            Filter();
            if (peer == selected) messages = messages.Select(m => unread.Any(u => u.Id == m.Id) ? m with { ReadAt = DateTime.UtcNow } : m).ToList();
        }
        catch (Exception ex) { if (error != null) error.Text = ex.Message; }
    }

    private async Task SendAsync()
    {
        if (sending || send?.IsEnabled != true || peer == 0 || string.IsNullOrWhiteSpace(input?.Text) && pendingFiles.Count == 0) return;
        string text = input!.Text.Trim();
        string signature;
        try { signature = text + string.Join("|", pendingFiles.Select(path => { var file = new FileInfo(path); return path + ":" + file.Length + ":" + file.LastWriteTimeUtc.Ticks; })); }
        catch (IOException ex) { if (error != null) error.Text = ex.Message; return; }

        if (pendingId == null || pendingText != signature || pendingPeer != peer) { pendingId = Guid.NewGuid().ToString("N"); pendingText = signature; pendingPeer = peer; }
        sending = true;
        send.IsEnabled = false;
        input.IsEnabled = false;
        if (users != null) users.IsEnabled = false;
        if (error != null) error.Text = "Отправляется…";
        pendingMessage = new ChatEntry(0, client.UserId, peer, text.Length > 0 ? text : "Вложения: " + string.Join(", ", pendingFiles.Select(Path.GetFileName)), pendingId, DateTime.UtcNow, null);
        pendingFailed = false;
        RenderHistory(true);

        int target = peer;
        try
        {
            if (pendingFiles.Count == 0) await client.SendAsync(target, text, pendingId);
            else await client.SendFilesAsync(target, text, pendingId, pendingFiles.ToArray());
            pendingMessage = null;
            if (peer == target && input.Text.Trim() == text) { input.Clear(); pendingFiles.Clear(); RenderPendingFiles(); pendingId = null; }
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            pendingFailed = true;
            if (error != null) error.Text = ex.Message;
            RenderHistory(true);
        }
        finally
        {
            sending = false;
            if (users != null) users.IsEnabled = true;
            input.IsEnabled = send.IsEnabled = contacts.FirstOrDefault(c => c.Id == peer)?.IsActive == true;
        }
    }

    private void ChooseFiles()
    {
        if (sending || peer == 0 || input?.IsEnabled != true) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "Все файлы|*.*" };
        if (dialog.ShowDialog(this) == true) AddFiles(dialog.FileNames);
    }

    internal void AddFiles(IEnumerable<string> paths)
    {
        if (sending || input?.IsEnabled != true || peer == 0) return;
        try
        {
            var proposed = pendingFiles.Concat(paths).Distinct().ToList();
            if (proposed.Count > 10 || proposed.Any(p => !File.Exists(p) || new FileInfo(p).Length > 50 * 1024 * 1024) || proposed.Sum(p => new FileInfo(p).Length) > 100 * 1024 * 1024)
                throw new IOException("Не более 10 файлов: до 50 МБ каждый и до 100 МБ всего.");
            pendingFiles.Clear();
            pendingFiles.AddRange(proposed);
            pendingId = null;
            RenderPendingFiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { if (error != null) error.Text = ex.Message; }
    }

    private void PasteImage()
    {
        var bitmap = Clipboard.GetImage();
        if (bitmap == null || peer == 0) return;
        AttachImage(bitmap);
    }

    internal void AttachImage(BitmapSource bitmap)
    {
        if (bitmap.PixelWidth * (long)bitmap.PixelHeight > 40_000_000) { if (error != null) error.Text = "Изображение слишком большое. Прикрепите его как файл."; return; }
        Directory.CreateDirectory(clipboardDirectory);
        string path = Path.Combine(clipboardDirectory, "Изображение-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + ".png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(path)) encoder.Save(file);
        AddFiles([path]);
    }

    private void RenderPendingFiles()
    {
        if (attachmentsPanel == null) return;
        attachmentsPanel.Children.Clear();
        foreach (string path in pendingFiles.ToArray())
        {
            var chip = ModernButton(Path.GetFileName(path) + " ×", (_, _) =>
            {
                if (sending) return;
                pendingFiles.Remove(path);
                pendingId = null;
                RenderPendingFiles();
            }, cornerRadius: 8, padding: new Thickness(8, 3, 8, 3));
            chip.FontSize = 11.5;
            chip.Margin = new Thickness(0, 0, 6, 4);
            chip.ToolTip = "Убрать вложение";
            attachmentsPanel.Children.Add(chip);
        }
    }

    private void SearchHistory()
    {
        if (peer == 0) return;
        int selected = peer;
        var dialog = new Window { Title = "Поиск по переписке", Width = 560, Height = 540, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        MessengerDialog.Apply(dialog, settings);
        var panel = new DockPanel { Margin = new Thickness(20) };
        var text = new TextBox { Padding = new Thickness(12), ToolTip = "Фраза от двух символов" };
        DockPanel.SetDock(text, Dock.Top);
        panel.Children.Add(text);
        var results = new ListBox { Margin = new Thickness(0, 12, 0, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        panel.Children.Add(results);
        dialog.Content = panel;
        long revision = 0;
        text.TextChanged += async (_, _) =>
        {
            long current = ++revision;
            await Task.Delay(200);
            if (current != revision) return;
            try
            {
                results.Items.Clear();
                if (text.Text.Trim().Length < 2) return;
                var rows = await client.SearchAsync(selected, text.Text);
                if (current != revision) return;
                foreach (var row in rows)
                    results.Items.Add(new TextBlock { Text = row.SentAt.ToLocalTime().ToString("dd.MM HH:mm") + " · " + row.Body, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
            }
            catch (Exception ex) { if (current == revision) results.Items.Add(ex.Message); }
        };
        dialog.ShowDialog();
    }

    private async Task LoadDetailsAsync()
    {
        if (!detailsOpen || details == null) return;
        details.Children.Clear();
        int selected = peer;
        var contact = contacts.FirstOrDefault(c => c.Id == selected);
        if (contact == null) return;

        details.Children.Add(new TextBlock { Text = contact.FullName, FontSize = FontSize + 4, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Foreground = ink });
        if (!contact.IsGroup)
        {
            details.Children.Add(new TextBlock { Text = contact.IsOnline ? "● В сети" : "○ Не в сети", Foreground = contact.IsOnline ? (Brush)new BrushConverter().ConvertFromString("#10B981")! : muted, FontSize = 12 });
            details.Children.Add(new TextBlock { Text = contact.Username + "\n" + contact.Branch, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Foreground = muted, FontSize = 12.5 });
            return;
        }

        try
        {
            var info = await client.GroupAsync(selected);
            if (selected != peer || details == null) return;
            details.Children.Add(new TextBlock { Text = info.Members.Count + " участников", Foreground = muted, FontSize = 12 });
            foreach (var member in info.Members)
            {
                var row = ModernButton("", (_, _) => { }, cornerRadius: 8);
                row.Content = new StackPanel { Children = { new TextBlock { Text = member.Label, TextWrapping = TextWrapping.Wrap, Foreground = ink }, new TextBlock { Text = member.Id == info.OwnerId ? "Создатель" : member.Detail, FontSize = 11, Foreground = muted, TextWrapping = TextWrapping.Wrap } } };
                bool owner = info.OwnerId == client.UserId;
                if ((owner || info.Members.Any(m => m.Id == client.UserId && m.IsAdmin)) && !info.IsClosed && member.Id != client.UserId && member.Id != info.OwnerId && (owner || !member.IsAdmin))
                {
                    var menu = new ContextMenu { Background = surface, Foreground = ink, BorderBrush = line };
                    void Add(string label, string operation, int minutes = 0)
                    {
                        var item = new MenuItem { Header = label };
                        item.Click += async (_, _) =>
                        {
                            if (MessageBox.Show(this, label + " — " + member.FullName + "?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                            try { await client.GroupOperationAsync(selected, operation, new { userId = member.Id, minutes }); await RefreshAsync(); } catch (Exception ex) { error!.Text = ex.Message; }
                        };
                        menu.Items.Add(item);
                    }
                    Add("Удалить из группы", "remove");
                    Add("Отключить на 1 час", "suspend", 60);
                    Add("Отключить на 1 день", "suspend", 1440);
                    Add("Восстановить доступ", "resume");
                    row.ContextMenu = menu;
                    if (owner)
                    {
                        Add(member.IsAdmin ? "Снять права администратора" : "Назначить администратором", member.IsAdmin ? "unadmin" : "admin");
                        Add("Передать владение", "owner");
                    }
                }
                details.Children.Add(row);
            }
            details.Children.Add(new TextBlock { Text = "Правый клик по группе в списке — настройки и приглашение участников.", Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), FontSize = 11.5 });
        }
        catch (Exception ex) { details.Children.Add(new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap, Foreground = muted }); }
    }

    private async Task SaveFileAsync(ChatAttachment file)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = file.Name, Filter = "Все файлы|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        await client.DownloadAsync(file, dialog.FileName);
    }

    private async Task OpenFileAsync(ChatAttachment file)
    {
        try
        {
            if (file.IsPdf)
            {
                Directory.CreateDirectory(clipboardDirectory);
                string pdfPath = Path.Combine(clipboardDirectory, Guid.NewGuid().ToString("N") + "_" + file.Name);
                await client.DownloadAsync(file, pdfPath);
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = pdfPath, UseShellExecute = true });
                    return;
                }
                catch
                {
                    await SaveFileAsync(file);
                    return;
                }
            }

            if (file.IsTextDocument)
            {
                Directory.CreateDirectory(clipboardDirectory);
                string textPath = Path.Combine(clipboardDirectory, Guid.NewGuid().ToString("N") + "_" + file.Name);
                try
                {
                    await client.DownloadAsync(file, textPath);
                    string content = "";
                    if (new FileInfo(textPath).Length > 2 * 1024 * 1024)
                    {
                        content = "Файл слишком большой для встроенного просмотра (> 2 МБ). Скачайте его для полного чтения.";
                    }
                    else
                    {
                        content = File.ReadAllText(textPath);
                    }

                    var panel = new DockPanel { Margin = new Thickness(12) };
                    var buttonsBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
                    DockPanel.SetDock(buttonsBar, Dock.Bottom);

                    var copyBtn = ModernButton("Копировать текст", (_, _) =>
                    {
                        try { Clipboard.SetText(content); MessageBox.Show("Текст скопирован в буфер обмена", "Успешно"); } catch { }
                    });
                    copyBtn.Margin = new Thickness(0, 0, 8, 0);

                    var openExternalBtn = ModernButton("Открыть в блокноте", (_, _) =>
                    {
                        try { Process.Start(new ProcessStartInfo { FileName = textPath, UseShellExecute = true }); } catch { }
                    });
                    openExternalBtn.Margin = new Thickness(0, 0, 8, 0);

                    var saveBtn = ModernButton("Сохранить как…", async (_, _) =>
                    {
                        try { await SaveFileAsync(file); } catch (Exception ex) { MessageBox.Show(ex.Message, "Сохранение"); }
                    }, primary: true);

                    buttonsBar.Children.Add(copyBtn);
                    buttonsBar.Children.Add(openExternalBtn);
                    buttonsBar.Children.Add(saveBtn);
                    panel.Children.Add(buttonsBar);

                    var textBox = new TextBox
                    {
                        Text = content,
                        IsReadOnly = true,
                        FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Background = Brushes.White,
                        Foreground = ink,
                        Padding = new Thickness(8),
                        BorderThickness = new Thickness(1),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225))
                    };
                    panel.Children.Add(textBox);

                    new Window
                    {
                        Title = "Просмотр документа · " + file.Name,
                        Content = panel,
                        Owner = this,
                        Width = 850,
                        Height = 620,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Background = surface,
                        Foreground = ink
                    }.ShowDialog();
                    return;
                }
                finally
                {
                    if (File.Exists(textPath)) { try { File.Delete(textPath); } catch { } }
                }
            }

            if (!file.IsImage) { await SaveFileAsync(file); return; }
            Directory.CreateDirectory(clipboardDirectory);
            string path = Path.Combine(clipboardDirectory, Guid.NewGuid().ToString("N"));
            try
            {
                await client.DownloadAsync(file, path);
                var panel = new DockPanel { Margin = new Thickness(12) };
                var download = ModernButton("Скачать изображение", async (_, _) => { try { await SaveFileAsync(file); } catch (Exception ex) { MessageBox.Show(ex.Message, "Скачивание"); } }, primary: true);
                DockPanel.SetDock(download, Dock.Bottom);
                panel.Children.Add(download);
                try
                {
                    BitmapSource image;
                    using (var stream = File.OpenRead(path))
                    {
                        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                        var frame = decoder.Frames[0];
                        if ((long)frame.PixelWidth * frame.PixelHeight > 100_000_000 || frame.PixelWidth == 0 || frame.PixelHeight == 0) throw new NotSupportedException();
                        stream.Position = 0;
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        double scale = Math.Min(1, Math.Min(1600d / frame.PixelWidth, 1200d / frame.PixelHeight));
                        bitmap.DecodePixelWidth = Math.Max(1, (int)(frame.PixelWidth * scale));
                        bitmap.EndInit();
                        bitmap.Freeze();
                        image = bitmap;
                    }
                    panel.Children.Add(new ScrollViewer { Content = new Image { Source = image, Stretch = Stretch.Uniform }, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
                }
                catch (Exception ex) when (ex is NotSupportedException or IOException or ArgumentException or FormatException)
                {
                    panel.Children.Add(new TextBlock { Text = "Предпросмотр этого изображения недоступен. Вы можете скачать оригинал.", TextWrapping = TextWrapping.Wrap, Foreground = ink });
                }
                new Window { Title = file.Name, Content = panel, Owner = this, Width = 800, Height = 600, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = surface, Foreground = ink }.ShowDialog();
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        catch (Exception ex) { if (error != null) error.Text = "Не удалось открыть вложение: " + ex.Message; }
    }
}
