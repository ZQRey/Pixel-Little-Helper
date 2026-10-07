using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Markup;
using System.IO;
using System.Windows.Media.Imaging;

namespace PixelHelper;
internal sealed class MessengerWindow : Window
{
    private readonly MessengerClient client;
    private readonly Settings settings;
    private readonly Grid root = new() { Margin = new Thickness(16) };
    private Brush surface = Brushes.White, ink = Brushes.Black, muted = Brushes.Gray, incoming = Brushes.White, outgoing = Brushes.LightCyan, accent = Brushes.Teal;
    private void ApplyTheme()
    {
        bool light = settings.ChatTheme == "Light", contrast = settings.ChatTheme == "Contrast";
        Brush Color(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
        Background = Color(light ? "#EDF2F7" : contrast ? "#000000" : settings.ChatTheme == "Dark" ? "#161B22" : "#102738");
        surface = Color(light ? "#FFFFFF" : contrast ? "#000000" : "#19384A");
        ink = Color(light ? "#163044" : "#F1F8FC"); muted = Color(light ? "#526879" : contrast ? "#FFFFFF" : "#ABC4D3");
        incoming = surface; outgoing = Color(light ? "#D3F4EF" : contrast ? "#000000" : "#235762"); accent = Color(contrast ? "#FFFF00" : "#36C7BA");
        Foreground = ink; FontFamily = new FontFamily("Segoe UI"); FontSize = Math.Clamp(settings.ChatFontSize, 11, 22);
        var textStyle = new Style(typeof(TextBox)); textStyle.Setters.Add(new Setter(Control.BackgroundProperty, surface)); textStyle.Setters.Add(new Setter(Control.ForegroundProperty, ink)); textStyle.Setters.Add(new Setter(Control.BorderBrushProperty, muted)); textStyle.Setters.Add(new Setter(TextBox.CaretBrushProperty, ink)); textStyle.Setters.Add(new Setter(Control.FontSizeProperty, FontSize)); Resources[typeof(TextBox)] = textStyle;
        var checkStyle = new Style(typeof(CheckBox)); checkStyle.Setters.Add(new Setter(Control.ForegroundProperty, ink)); Resources[typeof(CheckBox)] = checkStyle;
        Resources[typeof(Button)] = (Style)XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
              <Setter Property="Cursor" Value="Hand"/>
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
                <Border x:Name="Frame" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="7" Padding="{TemplateBinding Padding}">
                  <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Opacity" Value="0.8"/></Trigger>
                  <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Frame" Property="Opacity" Value="0.4"/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value></Setter>
            </Style>
            """);
    }
    private Brush ChatBackdrop()
    {
        if (settings.ChatBackground == "Image" && File.Exists(settings.ChatBackgroundImage))
        {
            try { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.UriSource = new Uri(settings.ChatBackgroundImage!, UriKind.Absolute); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 1600; bitmap.EndInit(); bitmap.Freeze(); return new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill, Opacity = 1 - Math.Clamp(settings.ChatBackgroundDim, 0, .85) }; }
            catch (Exception ex) { Settings.Log(ex); }
        }
        if (settings.ChatBackground == "Dots" || settings.ChatBackground == "Grid")
        {
            var group = new DrawingGroup(); var pen = new Pen(muted, .5);
            group.Children.Add(settings.ChatBackground == "Dots" ? new GeometryDrawing(muted, null, new EllipseGeometry(new Point(2, 2), 1, 1)) : new GeometryDrawing(null, pen, Geometry.Parse("M0,24 L0,0 24,0")));
            return new DrawingBrush(group) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 24, 24), ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 24, 24), Opacity = .16 };
        }
        return Brushes.Transparent;
    }
    private List<ChatContact> contacts = [];
    private List<ChatEntry> messages = [];
    private ListBox? users;
    private TextBox? search, input;
    private TextBlock? error, title;
    private StackPanel? history;
    private ScrollViewer? scroll;
    private Button? send;
    private int peer;
    private bool updating, closing, showingLogin;
    private string? pendingId, pendingText;
    private int pendingPeer;
    internal event Action<HelperEmoji>? EmojiInserted;
    internal int ActivePeer => IsActive ? peer : 0;
    internal MessengerWindow(MessengerClient client, Settings settings)
    {
        this.client = client; this.settings = settings;
        Title = "Мессенджер · PixelHelper"; Width = 880; Height = 650; MinWidth = 720; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Content = root; ApplyTheme();
        client.Changed += OnChanged;
        client.MessageReceived += OnMessage;
        Loaded += async (_, _) => await RefreshAsync();
        Activated += async (_, _) => await MarkReadAsync();
        Closed += (_, _) => { closing = true; client.Changed -= OnChanged; client.MessageReceived -= OnMessage; };
    }
    private void OnChanged() => Dispatcher.BeginInvoke(new Action(async () => { if (!closing) await RefreshAsync(); }));
    private void OnMessage(ChatEntry message) => OnChanged();
    private Button Button(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text, Background = surface, Foreground = ink, BorderBrush = accent, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(3) };
        button.Click += click; return button;
    }
    private void LoginForm()
    {
        showingLogin = true; users = null; peer = 0; contacts.Clear(); messages.Clear(); pendingId = null;
        root.Children.Clear(); root.ColumnDefinitions.Clear(); root.RowDefinitions.Clear();
        var panel = new StackPanel { Width = 360, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = "Автоматический вход Windows", FontSize = 22, Margin = new Thickness(0, 0, 0, 20) });
        panel.Children.Add(new TextBlock { Text = Environment.UserDomainName + "\\" + Environment.UserName, Margin = new Thickness(0, 0, 0, 12) });
        error = new TextBlock { Foreground = muted, TextWrapping = TextWrapping.Wrap, Text = client.SignInStatus }; panel.Children.Add(error);
        var submit = Button("Повторить подключение", async (sender, _) =>
        {
            ((Button)sender).IsEnabled = false;
            try { await client.SignInWindowsAsync(true); await RefreshAsync(); }
            finally { ((Button)sender).IsEnabled = true; }
        }); panel.Children.Add(submit);
        panel.Children.Add(new TextBlock { Text = "Используется учётная запись, под которой запущен помощник. Ввод пароля не требуется. При восстановлении связи вход повторится автоматически.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Foreground = muted });
        root.Children.Add(panel);
    }
    private void MainForm()
    {
        showingLogin = false; root.Children.Clear(); root.ColumnDefinitions.Clear(); root.RowDefinitions.Clear();
        root.ColumnDefinitions.Add(new() { Width = new GridLength(280) }); root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var toolbar = new WrapPanel();
        toolbar.Children.Add(new TextBlock { Text = client.FullName, FontWeight = FontWeights.Bold, Margin = new Thickness(4, 10, 15, 5) });
        toolbar.Children.Add(Button("Оформление и уведомления", async (_, _) => { var dialog = new MessengerPreferences(settings) { Owner = this }; if (dialog.ShowDialog() == true) { var draft = input?.Text; ApplyTheme(); MainForm(); input!.Text = draft ?? ""; Filter(); RenderHistory(false); await LoadHistoryAsync(); title!.Text = contacts.FirstOrDefault(c => c.Id == peer)?.FullName ?? "Выберите чат"; } }));
        toolbar.Children.Add(Button("Обновить соединение", async (_, _) => await client.SignInWindowsAsync(true)));
        toolbar.Children.Add(Button("Создать группу", async (_, _) => { var window = new ChatGroupWindow(client) { Owner = this }; if (window.ShowDialog() == true) { peer = window.CreatedPeer; messages.Clear(); pendingId = null; await RefreshAsync(); } }));
        toolbar.Children.Add(Button("Участники группы", async (_, _) => { if (peer >= 0) { error!.Text = "Выберите групповой чат."; return; } new ChatGroupWindow(client, peer) { Owner = this }.ShowDialog(); await RefreshAsync(); }));
        Grid.SetColumnSpan(toolbar, 2); root.Children.Add(toolbar);
        var left = new DockPanel { Margin = new Thickness(0, 8, 12, 8) };
        search = new TextBox { Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8), ToolTip = "Поиск по имени, логину или филиалу" };
        search.TextChanged += (_, _) => Filter(); DockPanel.SetDock(search, Dock.Top); left.Children.Add(search);
        Resources["ContactFont"] = FontSize; Resources["DetailFont"] = Math.Max(11, FontSize - 1); Resources["MutedInk"] = muted;
        users = new ListBox { Background = surface, Foreground = ink, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, ItemTemplate = (DataTemplate)XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Grid Margin="6,4">
                <Grid.RowDefinitions><RowDefinition/><RowDefinition/><RowDefinition/></Grid.RowDefinitions>
                <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                <TextBlock Text="{Binding Label}" TextTrimming="CharacterEllipsis" FontSize="{DynamicResource ContactFont}" Margin="0,0,5,4">
                  <TextBlock.Style><Style TargetType="TextBlock"><Style.Triggers><DataTrigger Binding="{Binding HasUnread}" Value="True"><Setter Property="FontWeight" Value="Bold"/></DataTrigger></Style.Triggers></Style></TextBlock.Style>
                </TextBlock>
                <Border Grid.Column="1" Background="#1678AB" CornerRadius="10" Padding="6,1" VerticalAlignment="Top">
                  <Border.Style><Style TargetType="Border"><Setter Property="Visibility" Value="Collapsed"/><Style.Triggers><DataTrigger Binding="{Binding HasUnread}" Value="True"><Setter Property="Visibility" Value="Visible"/></DataTrigger></Style.Triggers></Style></Border.Style>
                  <TextBlock Text="{Binding Unread}" Foreground="White" FontWeight="Bold" FontSize="11"/>
                </Border>
                <TextBlock Grid.Row="1" Grid.ColumnSpan="2" Text="{Binding Subtitle}" TextTrimming="CharacterEllipsis" Foreground="{DynamicResource MutedInk}" FontSize="{DynamicResource DetailFont}"/>
                <TextBlock Grid.Row="2" Grid.ColumnSpan="2" Text="{Binding TimeLabel}" Foreground="{DynamicResource MutedInk}" FontSize="10" Margin="0,4,0,0"/>
              </Grid>
            </DataTemplate>
            """) }; ScrollViewer.SetHorizontalScrollBarVisibility(users, ScrollBarVisibility.Disabled); users.SelectionChanged += async (_, _) =>
        {
            if (updating || users.SelectedItem is not ChatContact contact) return;
            if (peer != contact.Id) { peer = contact.Id; input!.Text = ""; pendingId = null; messages.Clear(); }
            await LoadHistoryAsync();
        }; left.Children.Add(users); Grid.SetRow(left, 1); root.Children.Add(left);
        var right = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
        title = new TextBlock { Text = "Выберите сотрудника", FontSize = FontSize + 4, Margin = new Thickness(4, 0, 4, 8) }; DockPanel.SetDock(title, Dock.Top); right.Children.Add(title);
        var older = Button("Предыдущие сообщения", async (_, _) =>
        {
            if (peer == 0 || messages.Count == 0) return;
            try { var previous = await client.HistoryAsync(peer, messages.Min(m => m.Id)); messages = previous.Concat(messages).DistinctBy(m => m.Id).OrderBy(m => m.Id).ToList(); RenderHistory(false); }
            catch (Exception ex) { error!.Text = ex.Message; }
        }); DockPanel.SetDock(older, Dock.Top); right.Children.Add(older);
        history = new StackPanel(); scroll = new ScrollViewer { Content = history, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = ChatBackdrop(), Padding = new Thickness(8) }; right.Children.Add(scroll);
        Grid.SetColumn(right, 1); Grid.SetRow(right, 1); root.Children.Add(right);
        var composer = new DockPanel(); send = Button("Отправить", async (_, _) => await SendAsync()); DockPanel.SetDock(send, Dock.Right); composer.Children.Add(send);
        var emojiButton = Button("Эмодзи", (_, _) => ShowEmojiPicker()); DockPanel.SetDock(emojiButton, Dock.Left); composer.Children.Add(emojiButton);
        input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 4000, MinHeight = 75, MaxHeight = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8) };
        input.KeyDown += async (_, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift) { e.Handled = true; await SendAsync(); } }; composer.Children.Add(input);
        var composerPanel = new StackPanel(); composerPanel.Children.Add(composer);
        var preview = new ContentControl { MaxHeight = 64, Margin = new Thickness(0, 4, 0, 0), ClipToBounds = true, Visibility = Visibility.Collapsed };
        input.ToolTip = "Фирменные эмодзи показываются в предпросмотре и в отправленном сообщении.";
        input.TextChanged += (_, _) => { preview.Visibility = HelperEmojis.Parse(input.Text).Any() ? Visibility.Visible : Visibility.Collapsed; if (preview.Visibility == Visibility.Visible) preview.Content = HelperEmojis.Render(input.Text, 28, settings.AnimatedChatEmojis); };
        composerPanel.Children.Add(preview); Grid.SetColumn(composerPanel, 1); Grid.SetRow(composerPanel, 2); root.Children.Add(composerPanel);
        error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) }; Grid.SetRow(error, 3); Grid.SetColumnSpan(error, 2); root.Children.Add(error);
    }
    private void Filter()
    {
        if (users == null) return;
        bool wasUpdating = updating; updating = true;
        string term = search?.Text.Trim() ?? "";
        users.ItemsSource = ChatContact.Ordered(contacts.Where(u => (u.FullName + " " + u.Username + " " + u.Branch).Contains(term, StringComparison.OrdinalIgnoreCase))).ToList();
        users.SelectedItem = contacts.FirstOrDefault(u => u.Id == peer); updating = wasUpdating;
    }
    private async Task RefreshAsync()
    {
        if (closing || updating) return;
        if (!client.SignedIn) { if (!showingLogin) LoginForm(); else if (error != null) error.Text = client.SignInStatus; return; }
        if (users == null || showingLogin) MainForm();
        updating = true;
        try { contacts = await client.UsersAsync(); if (peer < 0 && !contacts.Any(c => c.Id == peer)) { peer = 0; messages.Clear(); history?.Children.Clear(); if (title != null) title.Text = "Группа недоступна"; if (input != null) input.Clear(); pendingId = null; } Filter(); if (peer != 0) await LoadHistoryAsync(); }
        catch (Exception ex) { if (error != null) error.Text = ex.Message; }
        finally { updating = false; }
    }
    internal async Task OpenPeerAsync(int id) { peer = id; await RefreshAsync(); await LoadHistoryAsync(); Activate(); }
    private async Task LoadHistoryAsync()
    {
        if (peer == 0 || !client.SignedIn || closing) return;
        int selected = peer;
        try
        {
            var latest = await client.HistoryAsync(selected); if (selected != peer || closing) return;
            messages = messages.Where(m => selected < 0 ? m.RecipientId == selected : m.SenderId == selected || m.RecipientId == selected).Concat(latest).GroupBy(m => m.Id).Select(g => g.Last()).OrderBy(m => m.Id).ToList();
            title!.Text = contacts.FirstOrDefault(u => u.Id == peer)?.FullName ?? "Диалог";
            bool writable = contacts.FirstOrDefault(u => u.Id == peer)?.IsActive == true; input!.IsEnabled = writable; send!.IsEnabled = writable;
            RenderHistory(scroll!.ScrollableHeight - scroll.VerticalOffset < 5); await MarkReadAsync(); error!.Text = "";
        }
        catch (Exception ex) { if (error != null) error.Text = ex.Message; }
    }
    private void RenderHistory(bool bottom)
    {
        history!.Children.Clear();
        if (messages.Count == 0) history.Children.Add(new TextBlock { Text = "Начните переписку", Foreground = muted, Margin = new Thickness(12) });
        foreach (var message in messages)
        {
            bool mine = message.SenderId == client.UserId;
            var panel = new StackPanel();
            if (peer < 0) panel.Children.Add(new TextBlock { Text = message.SenderName ?? "Участник", FontWeight = FontWeights.Bold, Foreground = accent, Margin = new Thickness(0, 0, 0, 5) });
            var body = HelperEmojis.Render(message.Body, Math.Max(32, FontSize * 2.3), settings.AnimatedChatEmojis); body.FontSize = FontSize; body.Foreground = ink; panel.Children.Add(body);
            panel.Children.Add(new TextBlock { Text = message.SentAt.ToLocalTime().ToString("dd.MM HH:mm") + (mine ? peer < 0 || message.ReadAt == null ? " · Сохранено" : " · Прочитано" : ""), FontSize = Math.Max(11, FontSize - 2), Foreground = muted, Margin = new Thickness(0, 6, 0, 0) });
            history.Children.Add(new Border { Child = panel, Background = mine ? outgoing : incoming, BorderBrush = muted, BorderThickness = new Thickness(settings.ChatTheme == "Contrast" ? 1 : 0), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(4, 4, 4, 4), MaxWidth = Math.Max(280, ActualWidth * .48), HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left });
        }
        if (bottom) scroll!.ScrollToEnd();
    }
    private void ShowEmojiPicker()
    {
        if (input?.IsEnabled != true) return;
        var menu = new ContextMenu { Background = surface, Foreground = ink, PlacementTarget = input, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        foreach (var emoji in HelperEmojis.All)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var image = HelperEmojis.AnimatedImage(emoji, 32, settings.AnimatedChatEmojis);
            panel.Children.Add(image); panel.Children.Add(new TextBlock { Text = emoji.Name, VerticalAlignment = VerticalAlignment.Center });
            var item = new MenuItem { Header = panel }; item.Click += (_, _) => InsertEmoji(emoji); menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
    internal void InsertEmoji(HelperEmoji emoji)
    {
        if (input == null || !input.IsEnabled) return;
        if (input.Text.Length - input.SelectionLength + emoji.Code.Length > 4000) { error!.Text = "Сообщение не должно превышать 4000 символов."; return; }
        int index = input.SelectionStart; input.SelectedText = emoji.Code; input.CaretIndex = index + emoji.Code.Length; input.Focus();
        EmojiInserted?.Invoke(emoji);
    }
    private async Task MarkReadAsync()
    {
        if (!IsActive || peer == 0 || messages.Count == 0 || !client.SignedIn) return;
        var unread = messages.Where(m => (peer < 0 ? m.SenderId != client.UserId : m.RecipientId == client.UserId) && m.ReadAt == null).ToList();
        if (unread.Count == 0) return;
        int selected = peer; long through = unread.Max(m => m.Id);
        try
        {
            await client.ReadAsync(selected, through);
            contacts = contacts.Select(c => c.Id == selected && (c.LastId ?? 0) <= through ? c with { Unread = 0 } : c).ToList(); Filter();
            if (peer == selected) messages = messages.Select(m => unread.Any(u => u.Id == m.Id) ? m with { ReadAt = DateTime.UtcNow } : m).ToList();
        }
        catch (Exception ex) { if (error != null) error.Text = ex.Message; }
    }
    private async Task SendAsync()
    {
        if (send?.IsEnabled != true || peer == 0 || string.IsNullOrWhiteSpace(input?.Text)) return;
        string text = input.Text.Trim();
        if (pendingId == null || pendingText != text || pendingPeer != peer) { pendingId = Guid.NewGuid().ToString("N"); pendingText = text; pendingPeer = peer; }
        send.IsEnabled = false; error!.Text = "Отправляется…";
        int target = peer;
        try { await client.SendAsync(target, text, pendingId); if (peer == target && input.Text.Trim() == text) { input.Clear(); pendingId = null; } await LoadHistoryAsync(); }
        catch (Exception ex) { error.Text = ex.Message; }
        finally { send.IsEnabled = contacts.FirstOrDefault(c => c.Id == peer)?.IsActive == true; }
    }
}
