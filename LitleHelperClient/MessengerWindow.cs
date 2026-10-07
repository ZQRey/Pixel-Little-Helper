using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelHelper;
internal sealed class MessengerWindow : Window
{
    private readonly MessengerClient client;
    private readonly Settings settings;
    private readonly Grid root = new() { Margin = new Thickness(16) };
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
    internal int ActivePeer => IsActive ? peer : 0;
    internal MessengerWindow(MessengerClient client, Settings settings)
    {
        this.client = client; this.settings = settings;
        Title = "Мессенджер · PixelHelper"; Width = 880; Height = 650; MinWidth = 720; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = Brushes.WhiteSmoke; Content = root;
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
        var button = new Button { Content = text, Style = PetWindow.AssistantButtonStyle, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(3) };
        button.Click += click; return button;
    }
    private void LoginForm()
    {
        showingLogin = true; users = null; peer = 0; contacts.Clear(); messages.Clear(); pendingId = null;
        root.Children.Clear(); root.ColumnDefinitions.Clear(); root.RowDefinitions.Clear();
        var panel = new StackPanel { Width = 360, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = "Автоматический вход Windows", FontSize = 22, Margin = new Thickness(0, 0, 0, 20) });
        panel.Children.Add(new TextBlock { Text = Environment.UserDomainName + "\\" + Environment.UserName, Margin = new Thickness(0, 0, 0, 12) });
        error = new TextBlock { Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Text = client.SignInStatus }; panel.Children.Add(error);
        var submit = Button("Повторить подключение", async (sender, _) =>
        {
            ((Button)sender).IsEnabled = false;
            try { await client.SignInWindowsAsync(true); await RefreshAsync(); }
            finally { ((Button)sender).IsEnabled = true; }
        }); panel.Children.Add(submit);
        panel.Children.Add(new TextBlock { Text = "Используется учётная запись, под которой запущен помощник. Ввод пароля не требуется. При восстановлении связи вход повторится автоматически.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Foreground = Brushes.DimGray });
        root.Children.Add(panel);
    }
    private void MainForm()
    {
        showingLogin = false; root.Children.Clear(); root.ColumnDefinitions.Clear(); root.RowDefinitions.Clear();
        root.ColumnDefinitions.Add(new() { Width = new GridLength(270) }); root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var toolbar = new WrapPanel();
        toolbar.Children.Add(new TextBlock { Text = client.FullName, FontWeight = FontWeights.Bold, Margin = new Thickness(4, 10, 15, 5) });
        void Preference(string name, bool value, Action<bool> save)
        {
            var check = new CheckBox { Content = name, IsChecked = value, Margin = new Thickness(8, 10, 8, 5) };
            check.Click += (_, _) => { save(check.IsChecked == true); settings.Save(); }; toolbar.Children.Add(check);
        }
        Preference("Звук", settings.ChatSound, value => settings.ChatSound = value);
        Preference("Текст уведомления", settings.ChatPreview, value => settings.ChatPreview = value);
        Preference("Не беспокоить", settings.ChatDoNotDisturb, value => settings.ChatDoNotDisturb = value);
        toolbar.Children.Add(Button("Обновить соединение", async (_, _) => await client.SignInWindowsAsync(true)));
        Grid.SetColumnSpan(toolbar, 2); root.Children.Add(toolbar);
        var left = new DockPanel { Margin = new Thickness(0, 8, 12, 8) };
        search = new TextBox { Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8), ToolTip = "Поиск по имени, логину или филиалу" };
        search.TextChanged += (_, _) => Filter(); DockPanel.SetDock(search, Dock.Top); left.Children.Add(search);
        users = new ListBox(); users.SelectionChanged += async (_, _) =>
        {
            if (updating || users.SelectedItem is not ChatContact contact) return;
            if (peer != contact.Id) { peer = contact.Id; input!.Text = ""; pendingId = null; messages.Clear(); }
            await LoadHistoryAsync();
        }; left.Children.Add(users); Grid.SetRow(left, 1); root.Children.Add(left);
        var right = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
        title = new TextBlock { Text = "Выберите сотрудника", FontSize = 18, Margin = new Thickness(4, 0, 4, 8) }; DockPanel.SetDock(title, Dock.Top); right.Children.Add(title);
        var older = Button("Предыдущие сообщения", async (_, _) =>
        {
            if (peer == 0 || messages.Count == 0) return;
            try { var previous = await client.HistoryAsync(peer, messages.Min(m => m.Id)); messages = previous.Concat(messages).DistinctBy(m => m.Id).OrderBy(m => m.Id).ToList(); RenderHistory(false); }
            catch (Exception ex) { error!.Text = ex.Message; }
        }); DockPanel.SetDock(older, Dock.Top); right.Children.Add(older);
        history = new StackPanel(); scroll = new ScrollViewer { Content = history, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; right.Children.Add(scroll);
        Grid.SetColumn(right, 1); Grid.SetRow(right, 1); root.Children.Add(right);
        var composer = new DockPanel(); send = Button("Отправить", async (_, _) => await SendAsync()); DockPanel.SetDock(send, Dock.Right); composer.Children.Add(send);
        input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 4000, Height = 75, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8) };
        input.KeyDown += async (_, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift) { e.Handled = true; await SendAsync(); } }; composer.Children.Add(input);
        Grid.SetColumn(composer, 1); Grid.SetRow(composer, 2); root.Children.Add(composer);
        error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) }; Grid.SetRow(error, 3); Grid.SetColumnSpan(error, 2); root.Children.Add(error);
    }
    private void Filter()
    {
        if (users == null) return;
        bool wasUpdating = updating; updating = true;
        string term = search?.Text.Trim() ?? "";
        users.ItemsSource = contacts.Where(u => (u.FullName + " " + u.Username + " " + u.Branch).Contains(term, StringComparison.OrdinalIgnoreCase)).OrderByDescending(u => u.Unread > 0).ThenByDescending(u => u.LastId).ThenBy(u => u.FullName).ToList();
        users.SelectedItem = contacts.FirstOrDefault(u => u.Id == peer); updating = wasUpdating;
    }
    private async Task RefreshAsync()
    {
        if (closing || updating) return;
        if (!client.SignedIn) { if (!showingLogin) LoginForm(); else if (error != null) error.Text = client.SignInStatus; return; }
        if (users == null || showingLogin) MainForm();
        updating = true;
        try { contacts = await client.UsersAsync(); Filter(); if (peer != 0) await LoadHistoryAsync(); }
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
            messages = messages.Where(m => m.SenderId == selected || m.RecipientId == selected).Concat(latest).GroupBy(m => m.Id).Select(g => g.Last()).OrderBy(m => m.Id).ToList();
            title!.Text = contacts.FirstOrDefault(u => u.Id == peer)?.FullName ?? "Диалог";
            RenderHistory(scroll!.ScrollableHeight - scroll.VerticalOffset < 5); await MarkReadAsync(); error!.Text = "";
        }
        catch (Exception ex) { if (error != null) error.Text = ex.Message; }
    }
    private void RenderHistory(bool bottom)
    {
        history!.Children.Clear();
        if (messages.Count == 0) history.Children.Add(new TextBlock { Text = "Начните переписку", Foreground = Brushes.Gray, Margin = new Thickness(12) });
        foreach (var message in messages)
        {
            bool mine = message.SenderId == client.UserId;
            var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = message.Body, TextWrapping = TextWrapping.Wrap, FontSize = 14 });
            panel.Children.Add(new TextBlock { Text = message.SentAt.ToLocalTime().ToString("dd.MM HH:mm") + (mine ? message.ReadAt == null ? " · Сохранено" : " · Прочитано" : ""), FontSize = 11, Foreground = Brushes.DimGray, Margin = new Thickness(0, 6, 0, 0) });
            history.Children.Add(new Border { Child = panel, Background = mine ? Brushes.LightCyan : Brushes.White, CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(4, 4, 4, 4), MaxWidth = 420, HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left });
        }
        if (bottom) scroll!.ScrollToEnd();
    }
    private async Task MarkReadAsync()
    {
        if (!IsActive || peer == 0 || messages.Count == 0 || !client.SignedIn) return;
        var unread = messages.Where(m => m.RecipientId == client.UserId && m.ReadAt == null).ToList();
        if (unread.Count == 0) return;
        try { await client.ReadAsync(peer, unread.Max(m => m.Id)); messages = messages.Select(m => unread.Any(u => u.Id == m.Id) ? m with { ReadAt = DateTime.UtcNow } : m).ToList(); }
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
        finally { send.IsEnabled = true; }
    }
}
