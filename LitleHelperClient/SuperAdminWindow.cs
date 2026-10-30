using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelHelper;

public sealed record ClientNotice(string Text, string Sender = "", int DurationSeconds = 30, int? ChatPeerId = null)
{
    [System.Text.Json.Serialization.JsonIgnore] internal PetState? LocalAnimation { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] internal string? UrgentKey { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] internal long? UrgentMessageId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] internal DateTime? ExpiresAt { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] internal bool IsDiskAlert { get; init; }
}

public sealed record SuperAdminButton(int Id, string Title, string Message, string Target, int DurationSeconds, int OrderIndex, bool IsActive);
public sealed record NoticeComputer(string MachineName, bool IsOnline);

public sealed class SuperAdminWindow : Window
{
    private readonly Settings settings;
    private readonly PetWindow? petWindow;
    private readonly HttpClient http;
    private readonly CancellationTokenSource lifetime = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Grid rootGrid = new();
    private readonly ContentControl mainContent = new();
    private TextBlock statusText = null!;

    // Auth state
    private string? authToken;
    private string adminUsername = "";

    // Data
    private List<SuperAdminButton> buttons = [];
    private List<NoticeComputer> computers = [];

    // Navigation
    private Button btnTabNotice = null!;
    private Button btnTabTerminal = null!;
    private ContentControl moduleHost = null!;
    private int activeTab = 0; // 0 = Notice, 1 = Terminal

    // Notice module controls
    private TextBox txtNotice = null!;
    private TextBlock lblCharCount = null!;
    private CheckBox chkAnonymous = null!;
    private RadioButton rbTargetAll = null!;
    private RadioButton rbTargetOnline = null!;
    private RadioButton rbTargetSelect = null!;
    private Border targetListBorder = null!;
    private TextBox txtFilterPc = null!;
    private StackPanel targetsPanel = null!;
    private readonly List<CheckBox> pcCheckBoxes = [];
    private TextBox txtDuration = null!;
    private Button btnSendNotice = null!;
    private TextBlock noticeResultStatus = null!;

    // Terminal module controls
    private RadioButton rbShellPs = null!;
    private RadioButton rbShellCmd = null!;
    private CheckBox chkElevated = null!;
    private ComboBox cmbTargetPc = null!;
    private TextBox txtTerminalOutput = null!;
    private TextBox txtCommandLine = null!;
    private Button btnExecuteCommand = null!;
    private readonly List<string> commandHistory = [];
    private int historyIndex = -1;

    public SuperAdminWindow(Settings settings, PetWindow? petWindow = null)
    {
        this.settings = settings;
        this.petWindow = petWindow;

        Title = "Панель управления SuperAdmin";
        Width = 980;
        Height = 720;
        MinWidth = 800;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;
        Background = (Brush)new BrushConverter().ConvertFromString("#F8FAFC")!;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif");

        http = new(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(settings.ServerUrl!.TrimEnd('/') + "/api/"),
            Timeout = TimeSpan.FromSeconds(25)
        };
        http.DefaultRequestHeaders.Add("X-Client-Key", settings.ClientToken);
        http.DefaultRequestHeaders.Add("X-Machine-Name", Environment.MachineName);

        RenderLoginView();

        Closed += (_, _) =>
        {
            lifetime.Cancel();
            http.DefaultRequestHeaders.Authorization = null;
            http.Dispose();
        };
    }

    #region 1. Authentication View

    private void RenderLoginView()
    {
        var centerGrid = new Grid();
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(32),
            Width = 460,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var panel = new StackPanel();

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        titleRow.Children.Add(new TextBlock { Text = "⚡", FontSize = 22, Margin = new Thickness(0, 0, 10, 0) });
        titleRow.Children.Add(new TextBlock { Text = "Вход в панель SuperAdmin", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = (Brush)new BrushConverter().ConvertFromString("#0F172A")! });
        panel.Children.Add(titleRow);

        panel.Children.Add(new TextBlock
        {
            Text = "Доступ ограничен правами SuperAdmin. Данные аутентификации хранятся только в памяти сеанса.",
            FontSize = 12.5,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#64748B")!,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 20)
        });

        panel.Children.Add(new TextBlock { Text = "Источник авторизации", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var provider = new ComboBox { Height = 34, Margin = new Thickness(0, 0, 0, 14), FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
        provider.Items.Add("Active Directory (Доменная учётная запись)");
        provider.Items.Add("Локальная учётная запись Helper");
        provider.SelectedIndex = 0;
        panel.Children.Add(provider);

        panel.Children.Add(new TextBlock { Text = "Логин", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var login = new TextBox { Text = Environment.UserName, MaxLength = 100, Height = 34, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 0, 14) };
        panel.Children.Add(login);

        panel.Children.Add(new TextBlock { Text = "Пароль", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var password = new PasswordBox { MaxLength = 256, Height = 34, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 0, 20) };
        panel.Children.Add(password);

        var loginStatus = new TextBlock { FontSize = 12, Foreground = (Brush)new BrushConverter().ConvertFromString("#EF4444")!, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(loginStatus);

        var enter = new Button
        {
            Content = "Войти в систему",
            Height = 40,
            Background = (Brush)new BrushConverter().ConvertFromString("#2563EB")!,
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        panel.Children.Add(enter);

        async Task DoLoginAsync()
        {
            enter.IsEnabled = false;
            loginStatus.Text = "Проверка полномочий…";
            loginStatus.Foreground = (Brush)new BrushConverter().ConvertFromString("#64748B")!;

            try
            {
                var response = await CallAsync<JsonElement>(
                    provider.SelectedIndex == 0 ? "auth/ad" : "auth/login",
                    HttpMethod.Post,
                    new { username = login.Text.Trim(), password = password.Password }
                );

                authToken = response.GetProperty("token").GetString();
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
                password.Clear();

                adminUsername = login.Text.Trim();
                await LoadDataAsync();
                BuildMainShell();
            }
            catch (Exception ex)
            {
                password.Clear();
                http.DefaultRequestHeaders.Authorization = null;
                loginStatus.Foreground = (Brush)new BrushConverter().ConvertFromString("#EF4444")!;
                loginStatus.Text = ex.Message;
            }
            finally
            {
                enter.IsEnabled = true;
            }
        }

        enter.Click += async (_, _) => await DoLoginAsync();
        password.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await DoLoginAsync(); };

        card.Child = panel;
        centerGrid.Children.Add(card);
        Content = centerGrid;
    }

    private async Task<T> CallAsync<T>(string path, HttpMethod method, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null) request.Content = JsonContent.Create(body);

        using var response = await http.SendAsync(request, lifetime.Token);
        string content = await response.Content.ReadAsStringAsync(lifetime.Token);

        if (!response.IsSuccessStatusCode)
        {
            string error = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? "Неверный логин/пароль или сеанс истёк."
                : "Доступ запрещён. Требуется роль SuperAdmin.";
            try
            {
                using var document = JsonDocument.Parse(content);
                if (document.RootElement.TryGetProperty("error", out var detail))
                    error = detail.GetString() ?? error;
            }
            catch { }
            throw new InvalidOperationException(error);
        }

        return JsonSerializer.Deserialize<T>(content, Json)!;
    }

    private async Task LoadDataAsync()
    {
        buttons = await CallAsync<List<SuperAdminButton>>("super-client/buttons", HttpMethod.Get);
        computers = await CallAsync<List<NoticeComputer>>("super-client/computers", HttpMethod.Get);
    }

    #endregion

    #region 2. Main Shell Layout

    private void BuildMainShell()
    {
        rootGrid.Children.Clear();
        rootGrid.RowDefinitions.Clear();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Tabs bar
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Module host

        // 1. Top Header
        var header = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#0F172A")!,
            Padding = new Thickness(20, 14, 20, 14)
        };
        var hDock = new DockPanel();

        var hLeft = new StackPanel { Orientation = Orientation.Horizontal };
        hLeft.Children.Add(new TextBlock { Text = "⚡", FontSize = 22, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        var hTitles = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock { Text = "Панель SuperAdmin", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 10, 0) });

        var badge = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#DC2626")!,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "SUPERADMIN", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White }
        };
        titleRow.Children.Add(badge);
        hTitles.Children.Add(titleRow);

        hTitles.Children.Add(new TextBlock
        {
            Text = $"Администратор: {adminUsername}   ·   ПК управления: {Environment.MachineName}",
            FontSize = 12,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#94A3B8")!
        });
        hLeft.Children.Add(hTitles);
        hDock.Children.Add(hLeft);

        var hRight = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnReload = new Button
        {
            Content = "🔄 Обновить данные",
            FontSize = 12,
            Padding = new Thickness(12, 6, 12, 6),
            Background = (Brush)new BrushConverter().ConvertFromString("#334155")!,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        btnReload.Click += async (_, _) =>
        {
            await LoadDataAsync();
            RenderActiveModule();
        };
        hRight.Children.Add(btnReload);

        DockPanel.SetDock(hRight, Dock.Right);
        hDock.Children.Add(hRight);
        header.Child = hDock;
        Grid.SetRow(header, 0);
        rootGrid.Children.Add(header);

        // 2. Tabs Bar
        var tabsBar = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#E2E8F0")!,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(20, 10, 20, 10)
        };
        var tDock = new DockPanel();

        var tStack = new StackPanel { Orientation = Orientation.Horizontal };
        btnTabNotice = MakeTabButton("📢 Отправить сообщение", 0);
        btnTabTerminal = MakeTabButton("💻 Терминал", 1);
        tStack.Children.Add(btnTabNotice);
        tStack.Children.Add(btnTabTerminal);
        tDock.Children.Add(tStack);

        statusText = new TextBlock
        {
            FontSize = 12,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#64748B")!,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(statusText, Dock.Right);
        tDock.Children.Add(statusText);

        tabsBar.Child = tDock;
        Grid.SetRow(tabsBar, 1);
        rootGrid.Children.Add(tabsBar);

        // 3. Module Host
        moduleHost = new ContentControl();
        Grid.SetRow(moduleHost, 2);
        rootGrid.Children.Add(moduleHost);

        Content = rootGrid;
        UpdateTabStyles();
        RenderActiveModule();
    }

    private Button MakeTabButton(string text, int index)
    {
        var btn = new Button
        {
            Content = text,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 7, 16, 7),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!,
            Background = Brushes.White
        };
        btn.Click += (_, _) =>
        {
            activeTab = index;
            UpdateTabStyles();
            RenderActiveModule();
        };
        return btn;
    }

    private void UpdateTabStyles()
    {
        void StyleBtn(Button btn, bool active)
        {
            btn.Background = active
                ? (Brush)new BrushConverter().ConvertFromString("#2563EB")!
                : Brushes.White;
            btn.Foreground = active
                ? Brushes.White
                : (Brush)new BrushConverter().ConvertFromString("#334155")!;
            btn.BorderBrush = active
                ? (Brush)new BrushConverter().ConvertFromString("#2563EB")!
                : (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!;
        }

        StyleBtn(btnTabNotice, activeTab == 0);
        StyleBtn(btnTabTerminal, activeTab == 1);
    }

    private void RenderActiveModule()
    {
        if (activeTab == 0)
            RenderNoticeModule();
        else
            RenderTerminalModule();
    }

    #endregion

    #region 3. Module 0: Send Notice

    private void RenderNoticeModule()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel { Margin = new Thickness(24), MaxWidth = 840, HorizontalAlignment = HorizontalAlignment.Center };

        panel.Children.Add(new TextBlock
        {
            Text = "Рассылка уведомлений пользователям",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#0F172A")!,
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Отправка мгновенных всплывающих сообщений и оповещений на рабочие места сотрудников.",
            FontSize = 13,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#64748B")!,
            Margin = new Thickness(0, 0, 0, 16)
        });

        // 1. Templates Chips
        if (buttons.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = "Быстрые шаблоны:", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            var wrapChips = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            foreach (var b in buttons)
            {
                var chip = new Button
                {
                    Content = b.Title,
                    FontSize = 12,
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 0, 6, 6),
                    Background = (Brush)new BrushConverter().ConvertFromString("#F1F5F9")!,
                    BorderBrush = (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!,
                    Cursor = Cursors.Hand
                };
                chip.Click += (_, _) =>
                {
                    txtNotice.Text = b.Message;
                    txtDuration.Text = b.DurationSeconds.ToString();
                    if (b.Target == "all") rbTargetAll.IsChecked = true;
                };
                wrapChips.Children.Add(chip);
            }
            panel.Children.Add(wrapChips);
        }

        // 2. Message Text Input
        panel.Children.Add(new TextBlock { Text = "Текст сообщения *", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        txtNotice = new TextBox
        {
            Height = 110,
            MaxLength = 1000,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontSize = 13,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 4)
        };
        txtNotice.TextChanged += (_, _) =>
        {
            lblCharCount.Text = $"{txtNotice.Text.Length} / 1000 символов";
        };
        panel.Children.Add(txtNotice);

        lblCharCount = new TextBlock
        {
            Text = "0 / 1000 символов",
            FontSize = 11.5,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#94A3B8")!,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 14)
        };
        panel.Children.Add(lblCharCount);

        // 3. Anonymous Checkbox
        chkAnonymous = new CheckBox
        {
            Content = "🕶️ Анонимное сообщение (без указания имени администратора)",
            IsChecked = true,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 16)
        };
        panel.Children.Add(chkAnonymous);

        // 4. Target Selection Filters
        panel.Children.Add(new TextBlock { Text = "Получатели сообщения:", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var targetRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        rbTargetAll = new RadioButton { Content = "🌐 Всем клиентам", IsChecked = true, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 18, 0) };
        rbTargetOnline = new RadioButton { Content = "🟢 Только тем, кто онлайн", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 18, 0) };
        rbTargetSelect = new RadioButton { Content = "🎯 Выбранным компьютерам", FontWeight = FontWeights.SemiBold };
        targetRow.Children.Add(rbTargetAll);
        targetRow.Children.Add(rbTargetOnline);
        targetRow.Children.Add(rbTargetSelect);
        panel.Children.Add(targetRow);

        // 5. Target List Box (visible when rbTargetSelect is checked)
        targetListBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 16),
            Visibility = Visibility.Collapsed
        };
        var tlStack = new StackPanel();

        txtFilterPc = new TextBox
        {
            Height = 30,
            Padding = new Thickness(6, 0, 6, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Фильтр по имени компьютера",
            Margin = new Thickness(0, 0, 0, 8)
        };
        tlStack.Children.Add(txtFilterPc);

        targetsPanel = new StackPanel();
        pcCheckBoxes.Clear();
        foreach (var c in computers.OrderByDescending(x => x.IsOnline).ThenBy(x => x.MachineName))
        {
            var cb = new CheckBox
            {
                Content = $"{c.MachineName} {(c.IsOnline ? "· 🟢 Онлайн" : "· ⚪ Офлайн")}",
                Tag = c.MachineName,
                Margin = new Thickness(0, 2, 0, 2),
                FontSize = 12.5
            };
            pcCheckBoxes.Add(cb);
            targetsPanel.Children.Add(cb);
        }

        txtFilterPc.TextChanged += (_, _) =>
        {
            string q = txtFilterPc.Text.Trim().ToLowerInvariant();
            foreach (var cb in pcCheckBoxes)
            {
                cb.Visibility = string.IsNullOrEmpty(q) || cb.Tag.ToString()!.ToLowerInvariant().Contains(q)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        };

        var pcScroll = new ScrollViewer { Content = targetsPanel, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        tlStack.Children.Add(pcScroll);
        targetListBorder.Child = tlStack;
        panel.Children.Add(targetListBorder);

        void UpdateTargetSelectionUI()
        {
            targetListBorder.Visibility = rbTargetSelect.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
        rbTargetAll.Checked += (_, _) => UpdateTargetSelectionUI();
        rbTargetOnline.Checked += (_, _) => UpdateTargetSelectionUI();
        rbTargetSelect.Checked += (_, _) => UpdateTargetSelectionUI();

        // 6. Display Duration
        var durRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16), VerticalAlignment = VerticalAlignment.Center };
        durRow.Children.Add(new TextBlock { Text = "Время показа (секунды):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), FontWeight = FontWeights.SemiBold });
        txtDuration = new TextBox { Text = "30", Width = 60, Height = 32, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        durRow.Children.Add(txtDuration);

        foreach (var s in new[] { 15, 30, 60, 120 })
        {
            var btnSec = new Button { Content = $"{s}с", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand };
            btnSec.Click += (_, _) => txtDuration.Text = s.ToString();
            durRow.Children.Add(btnSec);
        }
        panel.Children.Add(durRow);

        // 7. Send Button
        btnSendNotice = new Button
        {
            Content = "📢 Отправить сообщение",
            Height = 44,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Background = (Brush)new BrushConverter().ConvertFromString("#2563EB")!,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 0, 12)
        };
        btnSendNotice.Click += async (_, _) => await SendNoticeAsync();
        panel.Children.Add(btnSendNotice);

        noticeResultStatus = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) };
        panel.Children.Add(noticeResultStatus);

        // 8. Emergency Testing & Reset Block
        var emCard = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 10, 0, 0)
        };
        var emStack = new StackPanel();
        emStack.Children.Add(new TextBlock { Text = "🧪 Тестирование экстренных кодов", FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
        emStack.Children.Add(new TextBlock
        {
            Text = "Безопасная локальная проверка: масштабирование робота и звуки отрабатывают только на этом ПК.",
            FontSize = 12,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#64748B")!,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var testWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var testList = new (string Title, string Code)[]
        {
            ("🔴 «Код красный»", "CODE_RED"),
            ("⚫ «Код чёрный»", "CODE_BLACK"),
            ("🟠 «Код оранжевый»", "CODE_ORANGE"),
            ("🟡 «Код жёлтый»", "CODE_YELLOW"),
            ("🔵 «Код синий»", "CODE_BLUE"),
            ("🌸 «Код розовый»", "CODE_PINK")
        };
        foreach (var item in testList)
        {
            var btn = new Button
            {
                Content = item.Title,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 8, 6),
                Cursor = Cursors.Hand,
                Background = (Brush)new BrushConverter().ConvertFromString("#F1F5F9")!,
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!
            };
            btn.Click += (_, _) =>
            {
                petWindow?.RunEmergencyTest(item.Code);
                noticeResultStatus.Text = $"Запущена локальная проверка {item.Title}.";
            };
            testWrap.Children.Add(btn);
        }
        emStack.Children.Add(testWrap);

        var btnCancelAlert = new Button
        {
            Content = "🛑 Сбросить / Отбой тревоги на всех ПК",
            Background = (Brush)new BrushConverter().ConvertFromString("#BE123C")!,
            Foreground = Brushes.White,
            Height = 36,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(16, 0, 16, 0)
        };
        btnCancelAlert.Click += async (_, _) =>
        {
            petWindow?.CancelActiveEmergency(isTest: false);
            try
            {
                await CallAsync<JsonElement>("settings/emergency/cancel", HttpMethod.Post);
                noticeResultStatus.Text = "Сигнал отбоя тревоги передан на сервер. Оповещения сняты на всех ПК.";
            }
            catch (Exception ex)
            {
                noticeResultStatus.Text = "Локально тревога сброшена. " + ex.Message;
            }
        };
        emStack.Children.Add(btnCancelAlert);
        emCard.Child = emStack;
        panel.Children.Add(emCard);

        scroll.Content = panel;
        moduleHost.Content = scroll;
    }

    private async Task SendNoticeAsync()
    {
        string text = txtNotice.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show("Введите текст сообщения.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            txtNotice.Focus();
            return;
        }

        if (!int.TryParse(txtDuration.Text, out int seconds) || seconds is < 10 or > 300)
        {
            MessageBox.Show("Время показа должно составлять от 10 до 300 секунд.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            txtDuration.Focus();
            return;
        }

        string[] targets;
        if (rbTargetAll.IsChecked == true)
        {
            targets = ["ALL"];
        }
        else if (rbTargetOnline.IsChecked == true)
        {
            targets = computers.Where(c => c.IsOnline).Select(c => c.MachineName).ToArray();
            if (targets.Length == 0)
            {
                MessageBox.Show("В данный момент нет компьютеров онлайн.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }
        else
        {
            targets = pcCheckBoxes.Where(cb => cb.IsChecked == true).Select(cb => cb.Tag.ToString()!).ToArray();
            if (targets.Length == 0)
            {
                MessageBox.Show("Пожалуйста, выберите хотя бы один компьютер из списка.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        btnSendNotice.IsEnabled = false;
        btnSendNotice.Content = "Отправка сообщения…";
        noticeResultStatus.Text = "Передача данных на сервер…";

        try
        {
            string sender = chkAnonymous.IsChecked == true ? "" : adminUsername;
            var results = await CallAsync<List<JsonElement>>("super-client/send", HttpMethod.Post, new
            {
                machines = targets,
                text,
                durationSeconds = seconds,
                sender
            });

            int delivered = results.Count(r => r.GetProperty("status").GetString() == "Pending");
            int offline = results.Count(r => r.GetProperty("status").GetString() == "Offline");
            noticeResultStatus.Foreground = (Brush)new BrushConverter().ConvertFromString("#15803D")!;
            noticeResultStatus.Text = $"✅ Сообщение успешно отправлено! Доставлено на {delivered} ПК; офлайн: {offline}. Подтверждения доступны в журнале панели.";
        }
        catch (Exception ex)
        {
            noticeResultStatus.Foreground = (Brush)new BrushConverter().ConvertFromString("#EF4444")!;
            noticeResultStatus.Text = "Ошибка отправки: " + ex.Message;
        }
        finally
        {
            btnSendNotice.IsEnabled = true;
            btnSendNotice.Content = "📢 Отправить сообщение";
        }
    }

    #endregion

    #region 4. Module 1: Remote Terminal

    private void RenderTerminalModule()
    {
        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Settings & Target Bar
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Quick Commands
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Console View
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Command Input

        // 1. Settings & Target Bar
        var topCard = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var tcStack = new StackPanel();

        var row1 = new DockPanel();

        var shellStack = new StackPanel { Orientation = Orientation.Horizontal };
        shellStack.Children.Add(new TextBlock { Text = "Оболочка:", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        rbShellPs = new RadioButton { Content = "PowerShell", IsChecked = true, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 14, 0) };
        rbShellCmd = new RadioButton { Content = "CMD", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 20, 0) };
        shellStack.Children.Add(rbShellPs);
        shellStack.Children.Add(rbShellCmd);

        chkElevated = new CheckBox
        {
            Content = "🛡️ С правами администратора домена (Elevated)",
            IsChecked = true,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#B45309")!,
            VerticalAlignment = VerticalAlignment.Center
        };
        shellStack.Children.Add(chkElevated);
        row1.Children.Add(shellStack);

        var pcSelectStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        pcSelectStack.Children.Add(new TextBlock { Text = "Целевой ПК:", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });

        cmbTargetPc = new ComboBox { Width = 260, Height = 32, FontSize = 12.5, VerticalContentAlignment = VerticalAlignment.Center };
        cmbTargetPc.Items.Add(new ComboBoxItem { Content = "🌐 [Все компьютеры онлайн]", Tag = "ALL" });
        foreach (var c in computers.OrderByDescending(x => x.IsOnline).ThenBy(x => x.MachineName))
        {
            cmbTargetPc.Items.Add(new ComboBoxItem
            {
                Content = $"{(c.IsOnline ? "🟢" : "⚪")} {c.MachineName} {(c.IsOnline ? "(Онлайн)" : "(Офлайн)")}",
                Tag = c.MachineName
            });
        }
        cmbTargetPc.SelectedIndex = 1; // Default to first PC if available, otherwise ALL
        pcSelectStack.Children.Add(cmbTargetPc);

        DockPanel.SetDock(pcSelectStack, Dock.Right);
        row1.Children.Add(pcSelectStack);
        tcStack.Children.Add(row1);

        topCard.Child = tcStack;
        Grid.SetRow(topCard, 0);
        grid.Children.Add(topCard);

        // 2. Quick Command Chips
        var chipsWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var quickCmds = new[]
        {
            "ipconfig /all",
            "whoami /all",
            "systeminfo",
            "quser",
            "gpupdate /force",
            "Get-Service Pixel*",
            "Get-Process | sort CPU -desc | select -first 10",
            "Clear"
        };
        foreach (var q in quickCmds)
        {
            var btnChip = new Button
            {
                Content = q,
                FontSize = 11.5,
                FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Background = (Brush)new BrushConverter().ConvertFromString("#F1F5F9")!,
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!,
                Cursor = Cursors.Hand
            };
            btnChip.Click += (_, _) =>
            {
                if (q == "Clear")
                {
                    txtTerminalOutput.Text = "";
                }
                else
                {
                    txtCommandLine.Text = q;
                    txtCommandLine.Focus();
                    txtCommandLine.CaretIndex = txtCommandLine.Text.Length;
                }
            };
            chipsWrap.Children.Add(btnChip);
        }
        Grid.SetRow(chipsWrap, 1);
        grid.Children.Add(chipsWrap);

        // 3. Terminal Console Output Window
        var consoleBorder = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#0D1117")!,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#30363D")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12)
        };
        txtTerminalOutput = new TextBox
        {
            Background = Brushes.Transparent,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#E6EDF3")!,
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            IsReadOnly = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = $"PixelHelper SuperAdmin Remote Terminal v1.2.83\nПодключено к: {settings.ServerUrl}\nДля выполнения команды введите текст внизу и нажмите Enter.\n--------------------------------------------------------------\n"
        };
        consoleBorder.Child = txtTerminalOutput;
        Grid.SetRow(consoleBorder, 2);
        grid.Children.Add(consoleBorder);

        // 4. Command Input Row
        var bottomGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

        txtCommandLine = new TextBox
        {
            Height = 38,
            FontSize = 13.5,
            FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"),
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 0, 10, 0),
            ToolTip = "Введите команду (например, ipconfig, hostname, Get-Service...)"
        };
        txtCommandLine.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                await ExecuteTerminalCommandAsync();
            }
            else if (e.Key == Key.Up)
            {
                if (commandHistory.Count > 0 && historyIndex > 0)
                {
                    historyIndex--;
                    txtCommandLine.Text = commandHistory[historyIndex];
                    txtCommandLine.CaretIndex = txtCommandLine.Text.Length;
                }
            }
            else if (e.Key == Key.Down)
            {
                if (historyIndex < commandHistory.Count - 1)
                {
                    historyIndex++;
                    txtCommandLine.Text = commandHistory[historyIndex];
                    txtCommandLine.CaretIndex = txtCommandLine.Text.Length;
                }
                else
                {
                    historyIndex = commandHistory.Count;
                    txtCommandLine.Text = "";
                }
            }
        };
        Grid.SetColumn(txtCommandLine, 0);
        bottomGrid.Children.Add(txtCommandLine);

        btnExecuteCommand = new Button
        {
            Content = "Выполнить",
            Height = 38,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Background = (Brush)new BrushConverter().ConvertFromString("#2563EB")!,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Margin = new Thickness(8, 0, 0, 0)
        };
        btnExecuteCommand.Click += async (_, _) => await ExecuteTerminalCommandAsync();
        Grid.SetColumn(btnExecuteCommand, 1);
        bottomGrid.Children.Add(btnExecuteCommand);

        Grid.SetRow(bottomGrid, 3);
        grid.Children.Add(bottomGrid);

        moduleHost.Content = grid;
        txtCommandLine.Focus();
    }

    private async Task ExecuteTerminalCommandAsync()
    {
        string cmd = txtCommandLine.Text.Trim();
        if (string.IsNullOrWhiteSpace(cmd)) return;

        // Command history
        commandHistory.Add(cmd);
        historyIndex = commandHistory.Count;
        txtCommandLine.Text = "";

        string targetMachine = (cmbTargetPc?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ALL";

        // Confirmation for ALL computers
        if (targetMachine == "ALL")
        {
            var res = MessageBox.Show(
                $"Внимание! Вы собираетесь выполнить команду:\n\n«{cmd}»\n\nодновременно на ВСЕХ компьютерах сети. Продолжить?",
                "Подтверждение групповой команды",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );
            if (res != MessageBoxResult.Yes) return;
        }

        string shellType = rbShellCmd?.IsChecked == true ? "cmd" : "powershell";
        bool elevated = chkElevated?.IsChecked == true;

        txtTerminalOutput.AppendText($"\n[{DateTime.Now:HH:mm:ss}] {targetMachine}> {cmd}\n");
        txtTerminalOutput.ScrollToEnd();

        btnExecuteCommand.IsEnabled = false;
        txtCommandLine.IsEnabled = false;

        try
        {
            string[] machines = targetMachine == "ALL"
                ? computers.Where(c => c.IsOnline).Select(c => c.MachineName).ToArray()
                : [targetMachine];

            if (machines.Length == 0)
            {
                txtTerminalOutput.AppendText("Ошибка: нет доступных компьютеров для отправки.\n");
                return;
            }

            var tasks = await CallAsync<List<JsonElement>>("commands", HttpMethod.Post, new
            {
                type = shellType,
                payload = cmd,
                machines,
                elevated
            });

            if (tasks.Count == 0)
            {
                txtTerminalOutput.AppendText("Команда не была поставлена в очередь.\n");
                return;
            }

            // Poll task outputs
            foreach (var t in tasks)
            {
                string taskId = t.GetProperty("taskId").GetString() ?? "";
                string machine = t.GetProperty("machineName").GetString() ?? "";

                if (string.IsNullOrEmpty(taskId)) continue;

                // Stream polling
                await PollTaskOutputAsync(taskId, machine);
            }
        }
        catch (Exception ex)
        {
            txtTerminalOutput.AppendText($"[Ошибка выполнения]: {ex.Message}\n");
            txtTerminalOutput.ScrollToEnd();
        }
        finally
        {
            btnExecuteCommand.IsEnabled = true;
            txtCommandLine.IsEnabled = true;
            txtCommandLine.Focus();
        }
    }

    private async Task PollTaskOutputAsync(string taskId, string machine)
    {
        int maxAttempts = 50; // ~15 seconds timeout
        int lastLen = 0;

        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                var task = await CallAsync<JsonElement>($"tasks/{taskId}", HttpMethod.Get);
                string status = task.TryGetProperty("status", out var sEl) ? sEl.GetString() ?? "" : "";
                string result = task.TryGetProperty("result", out var rEl) ? rEl.GetString() ?? "" : "";
                int exitCode = task.TryGetProperty("exitCode", out var eEl) ? eEl.GetInt32() : 0;

                if (result.Length > lastLen)
                {
                    string newChunk = result[lastLen..];
                    txtTerminalOutput.AppendText(newChunk);
                    txtTerminalOutput.ScrollToEnd();
                    lastLen = result.Length;
                }

                if (status is "Completed" or "Failed" or "Offline")
                {
                    txtTerminalOutput.AppendText($"\n[{machine}] Процесс завершён с кодом: {exitCode} ({status})\n");
                    txtTerminalOutput.ScrollToEnd();
                    return;
                }
            }
            catch { }

            await Task.Delay(350, lifetime.Token);
        }

        txtTerminalOutput.AppendText($"\n[{machine}] Время ожидания ответа задачи истекло.\n");
        txtTerminalOutput.ScrollToEnd();
    }

    #endregion
}
