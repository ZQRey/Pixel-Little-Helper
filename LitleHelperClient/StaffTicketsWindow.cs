using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelHelper;

public sealed class StaffTicketsWindow : Window
{
    private static (string Token, JsonElement User, DateTime ExpiresAt)? cachedSession;

    private readonly Settings settings;
    private readonly PetWindow? petWindow;
    private readonly HttpClient http;
    private readonly CancellationTokenSource lifetime = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Grid rootGrid = new();
    private readonly ContentControl mainContent = new();

    private string? authToken;
    private string technicianName = "";
    private string technicianRole = "";
    private int technicianGlpiId;

    private List<StaffTicketSummary> myTickets = [];
    private List<StaffTicketSummary> unassignedTickets = [];
    private List<StaffTicketSummary> closedTickets = [];

    private int activeTab = 0; // 0 = My, 1 = Unassigned, 2 = Closed
    
    public StaffTicketsWindow(Settings settings, PetWindow? petWindow = null)
    {
        this.settings = settings;
        this.petWindow = petWindow;

        Title = "Заявки HelpDesk";
        Width = 920;
        Height = 720;
        MinWidth = 760;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Background = (Brush)new BrushConverter().ConvertFromString("#F1F5F9")!;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        http = new(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(settings.ServerUrl!.TrimEnd('/') + "/api/"),
            Timeout = TimeSpan.FromSeconds(25)
        };
        http.DefaultRequestHeaders.Add("X-Client-Key", settings.ClientToken);
        http.DefaultRequestHeaders.Add("X-Machine-Name", Environment.MachineName);

        Content = rootGrid;
        rootGrid.Children.Add(mainContent);

        Closed += (_, _) =>
        {
            lifetime.Cancel();
            http.Dispose();
        };

        // Check if we have a valid in-memory session
        if (cachedSession != null && cachedSession.Value.ExpiresAt > DateTime.UtcNow)
        {
            authToken = cachedSession.Value.Token;
            ApplyTechnicianInfo(cachedSession.Value.User);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
            ShowMainWorkspace();
            _ = LoadOverviewAsync();
        }
        else
        {
            _ = TryAutoLoginAsync();
        }
    }

    private void ShowLoadingScreen(string message)
    {
        var container = new Grid
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#0F172A")!,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = "📋", FontSize = 36, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) });
        stack.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#E2E8F0")!,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        container.Children.Add(stack);
        mainContent.Content = container;
    }

    private async Task TryAutoLoginAsync()
    {
        ShowLoadingScreen("Проверка полномочий администратора…");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "staff-tickets/auto-auth");
            using var resp = await http.SendAsync(req, lifetime.Token);
            if (resp.IsSuccessStatusCode)
            {
                string json = await resp.Content.ReadAsStringAsync(lifetime.Token);
                using var doc = JsonDocument.Parse(json);
                string token = doc.RootElement.GetProperty("token").GetString()!;
                var userObj = doc.RootElement.GetProperty("user").Clone();

                authToken = token;
                ApplyTechnicianInfo(userObj);
                cachedSession = (token, userObj, DateTime.UtcNow.AddHours(8));
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

                ShowMainWorkspace();
                await LoadOverviewAsync();
                return;
            }
        }
        catch
        {
            // Fall back to manual login screen
        }

        ShowLoginScreen();
    }

    private void ApplyTechnicianInfo(JsonElement user)
    {
        technicianName = user.TryGetProperty("fullName", out var fn) && fn.GetString() is { Length: > 0 } s
            ? s
            : user.GetProperty("username").GetString() ?? "";
        technicianRole = user.TryGetProperty("role", out var r) ? r.GetString() ?? "" : "";
    }

    #region 1. Login Screen

    private void ShowLoginScreen()
    {
        var loginContainer = new Grid
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#0F172A")!,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var card = new Border
        {
            Width = 440,
            Background = Brushes.White,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(32),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 30,
                Opacity = 0.25,
                ShadowDepth = 8,
                Color = Colors.Black
            }
        };

        var stack = new StackPanel();

        var iconRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        iconRow.Children.Add(new TextBlock { Text = "📋", FontSize = 28, Margin = new Thickness(0, 0, 10, 0) });
        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock { Text = "Заявки HelpDesk", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = (Brush)new BrushConverter().ConvertFromString("#0F172A")! });
        titleStack.Children.Add(new TextBlock { Text = "Вход администратора техподдержки", FontSize = 12, Foreground = (Brush)new BrushConverter().ConvertFromString("#64748B")! });
        iconRow.Children.Add(titleStack);
        stack.Children.Add(iconRow);

        var note = new TextBlock
        {
            Text = "Доступно для Администраторов и Супер-администраторов. Данные загружаются напрямую с сервера и не сохраняются на диске.",
            FontSize = 11.5,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#64748B")!,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        };
        stack.Children.Add(note);

        stack.Children.Add(new TextBlock { Text = "Источник авторизации", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var provider = new ComboBox { Margin = new Thickness(0, 0, 0, 12), Height = 34, VerticalContentAlignment = VerticalAlignment.Center };
        provider.Items.Add("Active Directory (Доменная учётная запись)");
        provider.Items.Add("Локальная учётная запись Control Center");
        provider.SelectedIndex = 0;
        stack.Children.Add(provider);

        stack.Children.Add(new TextBlock { Text = "Имя пользователя / Логин", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var login = new TextBox { Text = Environment.UserName, MaxLength = 100, Height = 34, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(6, 0, 6, 0) };
        stack.Children.Add(login);

        stack.Children.Add(new TextBlock { Text = "Пароль", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var password = new PasswordBox { MaxLength = 256, Height = 34, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 16), Padding = new Thickness(6, 0, 6, 0) };
        stack.Children.Add(password);

        var errorText = new TextBlock
        {
            Foreground = (Brush)new BrushConverter().ConvertFromString("#EF4444")!,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
            Visibility = Visibility.Collapsed
        };
        stack.Children.Add(errorText);

        var enterBtn = new Button
        {
            Content = "Войти в систему заявок",
            Height = 38,
            Background = (Brush)new BrushConverter().ConvertFromString("#2563EB")!,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0)
        };
        stack.Children.Add(enterBtn);

        async void DoLogin()
        {
            enterBtn.IsEnabled = false;
            errorText.Visibility = Visibility.Collapsed;
            try
            {
                var body = new { username = login.Text.Trim(), password = password.Password };
                string path = provider.SelectedIndex == 0 ? "auth/ad" : "auth/login";

                using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
                using var response = await http.SendAsync(request, lifetime.Token);
                string resContent = await response.Content.ReadAsStringAsync(lifetime.Token);

                if (!response.IsSuccessStatusCode)
                {
                    string err = "Неверный логин или пароль.";
                    try
                    {
                        using var doc = JsonDocument.Parse(resContent);
                        if (doc.RootElement.TryGetProperty("error", out var e)) err = e.GetString() ?? err;
                    }
                    catch { }
                    throw new InvalidOperationException(err);
                }

                using var okDoc = JsonDocument.Parse(resContent);
                string token = okDoc.RootElement.GetProperty("token").GetString()!;
                var userObj = okDoc.RootElement.GetProperty("user").Clone();

                string role = userObj.TryGetProperty("role", out var r) ? r.GetString() ?? "" : "";
                if (role != "SuperAdmin" && role != "Admin")
                {
                    throw new InvalidOperationException("Доступ к управлению заявками разрешён только Администраторам и Супер-администраторам.");
                }

                authToken = token;
                ApplyTechnicianInfo(userObj);
                cachedSession = (token, userObj, DateTime.UtcNow.AddHours(8));
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
                password.Clear();

                ShowMainWorkspace();
                await LoadOverviewAsync();
            }
            catch (Exception ex)
            {
                password.Clear();
                errorText.Text = ex.Message;
                errorText.Visibility = Visibility.Visible;
            }
            finally
            {
                enterBtn.IsEnabled = true;
            }
        }

        enterBtn.Click += (_, _) => DoLogin();
        password.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) DoLogin(); };

        card.Child = stack;
        loginContainer.Children.Add(card);
        mainContent.Content = loginContainer;
    }

    #endregion

    #region 2. Main Workspace & Navigation

    private Button btnTabMy = null!;
    private Button btnTabUnassigned = null!;
    private Button btnTabClosed = null!;
    private TextBlock statusText = null!;
    private ContentControl moduleHost = null!;

    private void ShowMainWorkspace()
    {
        var mainLayout = new Grid();
        mainLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header bar
        mainLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Tabs & filters bar
        mainLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content

        // 1. Header Bar
        var headerBar = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#0F172A")!,
            Padding = new Thickness(18, 12, 18, 12)
        };
        var headerDock = new DockPanel();

        var titleBlock = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titleBlock.Children.Add(new TextBlock { Text = "📋", FontSize = 20, Margin = new Thickness(0, 0, 8, 0) });
        titleBlock.Children.Add(new TextBlock { Text = "Заявки HelpDesk", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(titleBlock, Dock.Left);
        headerDock.Children.Add(titleBlock);

        var rightStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        var userBadge = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#1E293B")!,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        userBadge.Child = new TextBlock
        {
            Text = $"👤 {technicianName} ({technicianRole})",
            FontSize = 12,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#94A3B8")!
        };
        rightStack.Children.Add(userBadge);

        var btnRefresh = new Button
        {
            Content = "↻ Обновить",
            Background = (Brush)new BrushConverter().ConvertFromString("#334155")!,
            Foreground = Brushes.White,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0)
        };
        btnRefresh.Click += async (_, _) => await LoadOverviewAsync();
        rightStack.Children.Add(btnRefresh);

        var btnLogout = new Button
        {
            Content = "Выйти",
            Background = Brushes.Transparent,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#94A3B8")!,
            Padding = new Thickness(8, 4, 8, 4),
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0)
        };
        btnLogout.Click += (_, _) =>
        {
            cachedSession = null;
            authToken = null;
            http.DefaultRequestHeaders.Authorization = null;
            ShowLoginScreen();
        };
        rightStack.Children.Add(btnLogout);

        DockPanel.SetDock(rightStack, Dock.Right);
        headerDock.Children.Add(rightStack);
        headerBar.Child = headerDock;
        Grid.SetRow(headerBar, 0);
        mainLayout.Children.Add(headerBar);

        // 2. Tabs Bar
        var tabsBar = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#E2E8F0")!,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(18, 10, 18, 10)
        };
        var tabsDock = new DockPanel();

        var tabButtons = new StackPanel { Orientation = Orientation.Horizontal };
        btnTabMy = MakeTabButton("💼 Мои заявки (0)", 0);
        btnTabUnassigned = MakeTabButton("📥 Не принятые заявки (0)", 1);
        btnTabClosed = MakeTabButton("🏁 Мои закрытые заявки (0)", 2);

        tabButtons.Children.Add(btnTabMy);
        tabButtons.Children.Add(btnTabUnassigned);
        tabButtons.Children.Add(btnTabClosed);
        DockPanel.SetDock(tabButtons, Dock.Left);
        tabsDock.Children.Add(tabButtons);

        statusText = new TextBlock
        {
            FontSize = 12,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#64748B")!,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(statusText, Dock.Right);
        tabsDock.Children.Add(statusText);

        tabsBar.Child = tabsDock;
        Grid.SetRow(tabsBar, 1);
        mainLayout.Children.Add(tabsBar);

        // 3. Content Module Host
        moduleHost = new ContentControl();
        Grid.SetRow(moduleHost, 2);
        mainLayout.Children.Add(moduleHost);

        mainContent.Content = mainLayout;
        UpdateTabStyles();
    }

    private Button MakeTabButton(string text, int tabIndex)
    {
        var btn = new Button
        {
            Content = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!,
            Background = Brushes.White
        };
        btn.Click += (_, _) =>
        {
            activeTab = tabIndex;
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

        StyleBtn(btnTabMy, activeTab == 0);
        StyleBtn(btnTabUnassigned, activeTab == 1);
        StyleBtn(btnTabClosed, activeTab == 2);
    }

    #endregion

    #region 3. Data Loading

    private async Task LoadOverviewAsync()
    {
        statusText.Text = "Загрузка данных…";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "staff-tickets/overview");
            using var res = await http.SendAsync(req, lifetime.Token);
            string content = await res.Content.ReadAsStringAsync(lifetime.Token);

            if (!res.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Ошибка получения заявок: " + res.StatusCode);
            }

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("technician", out var tech))
            {
                technicianGlpiId = tech.TryGetProperty("glpiUserId", out var gid) ? gid.GetInt32() : 0;
            }

            myTickets = root.GetProperty("myTickets").Deserialize<List<StaffTicketSummary>>(Json) ?? [];
            unassignedTickets = root.GetProperty("unassignedTickets").Deserialize<List<StaffTicketSummary>>(Json) ?? [];
            closedTickets = root.GetProperty("closedTickets").Deserialize<List<StaffTicketSummary>>(Json) ?? [];

            btnTabMy.Content = $"💼 Мои заявки ({myTickets.Count})";
            btnTabUnassigned.Content = $"📥 Не принятые заявки ({unassignedTickets.Count})";
            btnTabClosed.Content = $"🏁 Мои закрытые заявки ({closedTickets.Count})";

            statusText.Text = $"Обновлено в {DateTime.Now:HH:mm:ss}";
            RenderActiveModule();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            statusText.Text = "Ошибка: " + ex.Message;
        }
    }

    #endregion

    #region 4. Module Views Rendering

    private void RenderActiveModule()
    {
        if (activeTab == 0) RenderTicketsList("Мои активные заявки в работе", myTickets, canClaim: false, isClosed: false);
        else if (activeTab == 1) RenderTicketsList("Не принятые заявки (ожидают назначения)", unassignedTickets, canClaim: true, isClosed: false);
        else RenderClosedTicketsModule();
    }

    private void RenderTicketsList(string title, List<StaffTicketSummary> list, bool canClaim, bool isClosed)
    {
        var grid = new Grid { Margin = new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search bar
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Cards list

        var searchBox = new TextBox
        {
            Height = 34,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 0, 0, 12),
            ToolTip = "Поиск по номеру, теме, заявителю или кабинету…"
        };
        searchBox.SetValue(System.Windows.Controls.Primitives.TextBoxBase.SelectionBrushProperty, (Brush)new BrushConverter().ConvertFromString("#2563EB")!);

        var cardsPanel = new StackPanel();
        var scroll = new ScrollViewer { Content = cardsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);

        void ApplyListFilter()
        {
            cardsPanel.Children.Clear();
            string q = searchBox.Text.Trim().ToLowerInvariant();

            var filtered = list.Where(t =>
            {
                if (string.IsNullOrEmpty(q)) return true;
                return t.GlpiId.ToString().Contains(q) ||
                       t.Title.ToLowerInvariant().Contains(q) ||
                       t.Username.ToLowerInvariant().Contains(q) ||
                       t.UserFullName.ToLowerInvariant().Contains(q) ||
                       t.Room.ToLowerInvariant().Contains(q) ||
                       t.BranchName.ToLowerInvariant().Contains(q);
            }).ToList();

            if (filtered.Count == 0)
            {
                var empty = new Border
                {
                    Background = Brushes.White,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(30),
                    Margin = new Thickness(0, 10, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                empty.Child = new TextBlock
                {
                    Text = list.Count == 0 ? "В данном разделе нет заявок." : "По вашему запросу ничего не найдено.",
                    Foreground = (Brush)new BrushConverter().ConvertFromString("#64748B")!,
                    FontSize = 13
                };
                cardsPanel.Children.Add(empty);
                return;
            }

            foreach (var ticket in filtered)
            {
                cardsPanel.Children.Add(BuildTicketRowCard(ticket, canClaim, isClosed));
            }
        }

        searchBox.TextChanged += (_, _) => ApplyListFilter();
        Grid.SetRow(searchBox, 0);
        grid.Children.Add(searchBox);

        ApplyListFilter();
        moduleHost.Content = grid;
    }

    private void RenderClosedTicketsModule()
    {
        var grid = new Grid { Margin = new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Filter toolbar
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Cards list

        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };

        // Period filter
        var periodBox = new ComboBox { Height = 32, Margin = new Thickness(0, 0, 10, 6) };
        periodBox.Items.Add(new ComboBoxItem { Content = "📅 Период: За всё время", Tag = "all" });
        periodBox.Items.Add(new ComboBoxItem { Content = "📅 Период: За сегодня", Tag = "today" });
        periodBox.Items.Add(new ComboBoxItem { Content = "📅 Период: За 7 дней", Tag = "7days" });
        periodBox.Items.Add(new ComboBoxItem { Content = "📅 Период: За 30 дней", Tag = "30days" });
        periodBox.Items.Add(new ComboBoxItem { Content = "📅 Период: В этом месяце", Tag = "this_month" });
        periodBox.SelectedIndex = 0;
        toolbar.Children.Add(periodBox);

        // Score filter
        var scoreBox = new ComboBox { Height = 32, Margin = new Thickness(0, 0, 10, 6) };
        scoreBox.Items.Add(new ComboBoxItem { Content = "⭐️ Оценка: Все заявки", Tag = "all" });
        scoreBox.Items.Add(new ComboBoxItem { Content = "⭐️ Оценка: 5 звёзд (Отлично)", Tag = "5" });
        scoreBox.Items.Add(new ComboBoxItem { Content = "⭐️ Оценка: 4 звезды (Хорошо)", Tag = "4" });
        scoreBox.Items.Add(new ComboBoxItem { Content = "⭐️ Оценка: 3 звезды (Нормально)", Tag = "3" });
        scoreBox.Items.Add(new ComboBoxItem { Content = "⭐️ Оценка: 2 звезды (Плохо)", Tag = "2" });
        scoreBox.Items.Add(new ComboBoxItem { Content = "⭐️ Оценка: 1 звезда (Ужасно)", Tag = "1" });
        scoreBox.Items.Add(new ComboBoxItem { Content = "⭐️ Оценка: Без оценки", Tag = "none" });
        scoreBox.SelectedIndex = 0;
        toolbar.Children.Add(scoreBox);

        // Search text
        var searchBox = new TextBox
        {
            Height = 32,
            Width = 240,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(0, 0, 0, 6),
            ToolTip = "Поиск по номеру, теме, заявителю…"
        };
        toolbar.Children.Add(searchBox);

        Grid.SetRow(toolbar, 0);
        grid.Children.Add(toolbar);

        var cardsPanel = new StackPanel();
        var scroll = new ScrollViewer { Content = cardsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);

        void ApplyClosedFilter()
        {
            cardsPanel.Children.Clear();
            string period = (periodBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
            string scoreFilter = (scoreBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
            string q = searchBox.Text.Trim().ToLowerInvariant();

            DateTime now = DateTime.UtcNow;

            var filtered = closedTickets.Where(t =>
            {
                // Period
                if (period == "today")
                {
                    DateTime refDate = t.ClosedAt ?? t.CreatedAt;
                    if (refDate.Date != now.Date) return false;
                }
                else if (period == "7days")
                {
                    DateTime refDate = t.ClosedAt ?? t.CreatedAt;
                    if ((now - refDate).TotalDays > 7) return false;
                }
                else if (period == "30days")
                {
                    DateTime refDate = t.ClosedAt ?? t.CreatedAt;
                    if ((now - refDate).TotalDays > 30) return false;
                }
                else if (period == "this_month")
                {
                    DateTime refDate = t.ClosedAt ?? t.CreatedAt;
                    if (refDate.Year != now.Year || refDate.Month != now.Month) return false;
                }

                // Score
                if (scoreFilter == "none")
                {
                    if (t.Score.HasValue && t.Score.Value > 0) return false;
                }
                else if (scoreFilter is "1" or "2" or "3" or "4" or "5")
                {
                    if (!t.Score.HasValue || Math.Round(t.Score.Value).ToString() != scoreFilter) return false;
                }

                // Text search
                if (!string.IsNullOrEmpty(q))
                {
                    bool match = t.GlpiId.ToString().Contains(q) ||
                                 t.Title.ToLowerInvariant().Contains(q) ||
                                 t.Username.ToLowerInvariant().Contains(q) ||
                                 t.UserFullName.ToLowerInvariant().Contains(q) ||
                                 t.Room.ToLowerInvariant().Contains(q);
                    if (!match) return false;
                }

                return true;
            }).ToList();

            if (filtered.Count == 0)
            {
                var empty = new Border
                {
                    Background = Brushes.White,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(30),
                    Margin = new Thickness(0, 10, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                empty.Child = new TextBlock
                {
                    Text = "Заявки не найдены по выбранным фильтрам.",
                    Foreground = (Brush)new BrushConverter().ConvertFromString("#64748B")!,
                    FontSize = 13
                };
                cardsPanel.Children.Add(empty);
                return;
            }

            foreach (var ticket in filtered)
            {
                cardsPanel.Children.Add(BuildTicketRowCard(ticket, canClaim: false, isClosed: true));
            }
        }

        periodBox.SelectionChanged += (_, _) => ApplyClosedFilter();
        scoreBox.SelectionChanged += (_, _) => ApplyClosedFilter();
        searchBox.TextChanged += (_, _) => ApplyClosedFilter();

        ApplyClosedFilter();
        moduleHost.Content = grid;
    }

    private UIElement BuildTicketRowCard(StaffTicketSummary ticket, bool canClaim, bool isClosed)
    {
        var card = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(8),
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#E2E8F0")!,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var mainDock = new DockPanel();

        // Right-side actions
        var rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        if (canClaim)
        {
            var btnClaim = new Button
            {
                Content = "📥 Принять на себя",
                Background = (Brush)new BrushConverter().ConvertFromString("#16A34A")!,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0)
            };
            btnClaim.Click += async (_, _) => await ClaimTicketAsync(ticket);
            rightStack.Children.Add(btnClaim);
        }

        var btnOpen = new Button
        {
            Content = canClaim ? "🔍 Ознакомиться" : "🔍 Карточка заявки",
            Background = (Brush)new BrushConverter().ConvertFromString("#2563EB")!,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(10, 6, 10, 6),
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0)
        };
        btnOpen.Click += async (_, _) => await OpenTicketCardAsync(ticket.Id, canClaim, isClosed);
        rightStack.Children.Add(btnOpen);

        DockPanel.SetDock(rightStack, Dock.Right);
        mainDock.Children.Add(rightStack);

        // Left info
        var infoStack = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };

        var topRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        topRow.Children.Add(new TextBlock
        {
            Text = $"#{ticket.GlpiId}",
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#0F172A")!,
            Margin = new Thickness(0, 0, 8, 0)
        });

        topRow.Children.Add(new TextBlock
        {
            Text = ticket.Title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#1E293B")!,
            Margin = new Thickness(0, 0, 8, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 450
        });

        topRow.Children.Add(BuildStatusBadge(ticket.Status, ticket.StatusName));
        infoStack.Children.Add(topRow);

        var midRow = new TextBlock
        {
            Text = $"👤 {ticket.UserFullName} ({ticket.Username})  ·  🏢 {ticket.BranchName}  ·  🚪 Каб. {ticket.Room}  ·  💻 {ticket.MachineName}",
            FontSize = 12,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#64748B")!,
            Margin = new Thickness(0, 0, 0, 4)
        };
        infoStack.Children.Add(midRow);

        var btmRow = new StackPanel { Orientation = Orientation.Horizontal };
        btmRow.Children.Add(new TextBlock
        {
            Text = $"📅 Создана: {ticket.CreatedAt:dd.MM.yyyy HH:mm}",
            FontSize = 11.5,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#94A3B8")!,
            Margin = new Thickness(0, 0, 14, 0)
        });

        if (!string.IsNullOrWhiteSpace(ticket.AssignedUsername))
        {
            btmRow.Children.Add(new TextBlock
            {
                Text = $"👨‍🔧 Исполнитель: {ticket.AssignedUsername}",
                FontSize = 11.5,
                Foreground = (Brush)new BrushConverter().ConvertFromString("#0284C7")!,
                Margin = new Thickness(0, 0, 14, 0)
            });
        }

        // Show Score if closed and rated
        if (isClosed)
        {
            if (ticket.Score.HasValue && ticket.Score.Value > 0)
            {
                int s = (int)Math.Round(ticket.Score.Value);
                string stars = new string('★', Math.Clamp(s, 1, 5));
                var scoreBadge = new Border
                {
                    Background = (Brush)new BrushConverter().ConvertFromString("#FEF3C7")!,
                    BorderBrush = (Brush)new BrushConverter().ConvertFromString("#F59E0B")!,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 1, 6, 1)
                };
                scoreBadge.Child = new TextBlock
                {
                    Text = $"⭐️ {stars} ({ticket.Score.Value:0.0})",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)new BrushConverter().ConvertFromString("#B45309")!
                };
                btmRow.Children.Add(scoreBadge);
            }
            else
            {
                btmRow.Children.Add(new TextBlock
                {
                    Text = "Нет оценки",
                    FontSize = 11.5,
                    Foreground = (Brush)new BrushConverter().ConvertFromString("#94A3B8")!
                });
            }
        }

        infoStack.Children.Add(btmRow);
        mainDock.Children.Add(infoStack);

        card.Child = mainDock;
        return card;
    }

        private static int ParseStatus(string? status)
    {
        if (int.TryParse(status, out int s)) return s;
        return (status ?? "").Trim().ToLowerInvariant() switch
        {
            "new" => 1,
            "assigned" or "processing" => 2,
            "planned" => 3,
            "waiting" or "pending" => 4,
            "solved" => 5,
            "closed" => 6,
            _ => 1
        };
    }

    private static Border BuildStatusBadge(string status, string statusName)
    {
        string bg = "#E2E8F0";
        string fg = "#475569";
        switch (ParseStatus(status))
        {
            case 1: bg = "#DBEAFE"; fg = "#1D4ED8"; break; // Новая
            case 2: bg = "#FEF3C7"; fg = "#B45309"; break; // В работе
            case 3: bg = "#FEF3C7"; fg = "#B45309"; break; // В работе
            case 4: bg = "#F3E8FF"; fg = "#7E22CE"; break; // Ожидание
            case 5: bg = "#DCFCE7"; fg = "#15803D"; break; // Выполнена
            case 6: bg = "#E2E8F0"; fg = "#334155"; break; // Закрыта
        }

        var b = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString(bg)!,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        b.Child = new TextBlock
        {
            Text = statusName,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)new BrushConverter().ConvertFromString(fg)!
        };
        return b;
    }

    #endregion

    #region 5. Ticket Card Detailed View & Actions

    private async Task ClaimTicketAsync(StaffTicketSummary ticket)
    {
        if (MessageBox.Show($"Принять заявку #{ticket.GlpiId} «{ticket.Title}» на себя?", "Принятие заявки", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"staff-tickets/{ticket.Id}/claim");
            using var res = await http.SendAsync(req, lifetime.Token);
            if (!res.IsSuccessStatusCode)
            {
                string err = await res.Content.ReadAsStringAsync(lifetime.Token);
                throw new InvalidOperationException("Ошибка принятия заявки: " + err);
            }

            MessageBox.Show($"Заявка #{ticket.GlpiId} успешно принята на вас и переведена в статус «В работе».", "Заявка принята", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadOverviewAsync();
            activeTab = 0; // Switch to My Tickets
            UpdateTabStyles();
            RenderActiveModule();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task OpenTicketCardAsync(int ticketId, bool canClaim, bool isClosed)
    {
        statusText.Text = "Загрузка карточки заявки…";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"staff-tickets/{ticketId}");
            using var res = await http.SendAsync(req, lifetime.Token);
            string content = await res.Content.ReadAsStringAsync(lifetime.Token);

            if (!res.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Не удалось загрузить данные карточки заявки.");
            }

            var detail = JsonSerializer.Deserialize<StaffTicketDetail>(content, Json);
            if (detail == null) return;

            statusText.Text = $"Карточка #{detail.Ticket.GlpiId}";
            RenderTicketCard(detail, canClaim, isClosed);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            statusText.Text = "Ошибка загрузки";
        }
    }

    private void RenderTicketCard(StaffTicketDetail detail, bool canClaim, bool isClosed)
    {
        var ticket = detail.Ticket;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Back & top actions
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Scrollable body

        // Top bar
        var topDock = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };

        var btnBack = new Button
        {
            Content = "← Назад к списку",
            Background = (Brush)new BrushConverter().ConvertFromString("#334155")!,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(12, 6, 12, 6),
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0)
        };
        btnBack.Click += (_, _) => RenderActiveModule();
        DockPanel.SetDock(btnBack, Dock.Left);
        topDock.Children.Add(btnBack);

        // Status changer & claim button
        var actionDockRight = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        if (canClaim)
        {
            var btnClaim = new Button
            {
                Content = "📥 Принять заявку на себя",
                Background = (Brush)new BrushConverter().ConvertFromString("#16A34A")!,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(14, 6, 14, 6),
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0)
            };
            btnClaim.Click += async (_, _) =>
            {
                await ClaimTicketAsync(ticket);
            };
            actionDockRight.Children.Add(btnClaim);
        }
        else if (!isClosed)
        {
            var statusBox = new ComboBox { Height = 32, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Изменить статус заявки" };
            statusBox.Items.Add(new ComboBoxItem { Content = "🟡 В работе (назначена)", Tag = 2 });
            statusBox.Items.Add(new ComboBoxItem { Content = "🟣 В ожидании (уточнение)", Tag = 4 });
            statusBox.Items.Add(new ComboBoxItem { Content = "🟢 Выполнена (решена)", Tag = 5 });
            statusBox.Items.Add(new ComboBoxItem { Content = "⚪ Закрыта (окончательно)", Tag = 6 });

            int curSt = ParseStatus(ticket.Status);
            for (int i = 0; i < statusBox.Items.Count; i++)
            {
                if ((int)(statusBox.Items[i] as ComboBoxItem)!.Tag == curSt) statusBox.SelectedIndex = i;
            }

            var btnChangeStatus = new Button
            {
                Content = "Сменить статус",
                Background = (Brush)new BrushConverter().ConvertFromString("#475569")!,
                Foreground = Brushes.White,
                Padding = new Thickness(10, 6, 10, 6),
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0)
            };
            btnChangeStatus.Click += async (_, _) =>
            {
                if (statusBox.SelectedItem is ComboBoxItem item && item.Tag is int newSt)
                {
                    await ChangeTicketStatusAsync(ticket.Id, newSt);
                }
            };

            actionDockRight.Children.Add(statusBox);
            actionDockRight.Children.Add(btnChangeStatus);
        }

        DockPanel.SetDock(actionDockRight, Dock.Right);
        topDock.Children.Add(actionDockRight);

        Grid.SetRow(topDock, 0);
        root.Children.Add(topDock);

        // Body
        var bodyStack = new StackPanel();
        var scroll = new ScrollViewer { Content = bodyStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        // 1. Title Card
        var titleCard = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(8),
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 14)
        };
        var tcStack = new StackPanel();

        var tRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        tRow.Children.Add(new TextBlock { Text = $"#{ticket.GlpiId} · {ticket.Title}", FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 10, 0) });
        tRow.Children.Add(BuildStatusBadge(ticket.Status, ticket.StatusName));
        tcStack.Children.Add(tRow);

        var metaText = new TextBlock
        {
            Text = $"👤 Заявитель: {ticket.UserFullName} ({ticket.Username})\n🏢 Филиал: {ticket.BranchName}  ·  🚪 Кабинет: {ticket.Room}  ·  💻 Компьютер: {ticket.MachineName}\n📅 Дата создания: {ticket.CreatedAt:dd.MM.yyyy HH:mm}   |   👨‍🔧 Назначено: {(!string.IsNullOrEmpty(ticket.AssignedUsername) ? ticket.AssignedUsername : "Не назначен")}",
            FontSize = 12.5,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#475569")!,
            LineHeight = 20,
            Margin = new Thickness(0, 0, 0, 12)
        };
        tcStack.Children.Add(metaText);

        tcStack.Children.Add(new TextBlock { Text = "Описание проблемы:", FontWeight = FontWeights.SemiBold, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 4) });
        var descBorder = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#F8FAFC")!,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#E2E8F0")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12)
        };
        descBorder.Child = new TextBlock { Text = ticket.Description, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19 };
        tcStack.Children.Add(descBorder);

        titleCard.Child = tcStack;
        bodyStack.Children.Add(titleCard);

        // 2. Score & User Feedback (if rated)
        if (ticket.Score.HasValue && ticket.Score.Value > 0)
        {
            var scoreCard = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString("#FEF3C7")!,
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#F59E0B")!,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 14)
            };
            var scStack = new StackPanel();
            scStack.Children.Add(new TextBlock
            {
                Text = $"⭐️ Оценка пользователя: {new string('★', (int)Math.Round(ticket.Score.Value))} ({ticket.Score.Value:0.0} из 5)",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = (Brush)new BrushConverter().ConvertFromString("#92400E")!,
                Margin = new Thickness(0, 0, 0, 4)
            });
            if (!string.IsNullOrWhiteSpace(ticket.ScoreComment))
            {
                scStack.Children.Add(new TextBlock
                {
                    Text = $"Отзыв заявителя: «{ticket.ScoreComment}»",
                    FontSize = 12.5,
                    Foreground = (Brush)new BrushConverter().ConvertFromString("#78350F")!,
                    TextWrapping = TextWrapping.Wrap
                });
            }
            scoreCard.Child = scStack;
            bodyStack.Children.Add(scoreCard);
        }

        // 3. Solutions (if any)
        if (detail.Solutions.Count > 0)
        {
            foreach (var sol in detail.Solutions)
            {
                var solCard = new Border
                {
                    Background = (Brush)new BrushConverter().ConvertFromString("#DCFCE7")!,
                    BorderBrush = (Brush)new BrushConverter().ConvertFromString("#86EFAC")!,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14),
                    Margin = new Thickness(0, 0, 0, 14)
                };
                var sStack = new StackPanel();
                sStack.Children.Add(new TextBlock
                {
                    Text = $"✅ Решение проблемы {(sol.Date.HasValue ? "(" + sol.Date.Value.ToString("dd.MM.yyyy HH:mm") + ")" : "")}:",
                    FontWeight = FontWeights.Bold,
                    FontSize = 13,
                    Foreground = (Brush)new BrushConverter().ConvertFromString("#166534")!,
                    Margin = new Thickness(0, 0, 0, 6)
                });
                sStack.Children.Add(new TextBlock { Text = sol.Content, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 18 });
                solCard.Child = sStack;
                bodyStack.Children.Add(solCard);
            }
        }

        // 4. Followups History
        var historyHeader = new TextBlock
        {
            Text = $"💬 История ответов и переписки ({detail.Followups.Count})",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 4, 0, 8)
        };
        bodyStack.Children.Add(historyHeader);

        if (detail.Followups.Count == 0)
        {
            bodyStack.Children.Add(new TextBlock
            {
                Text = "Ответов по данной заявке ещё нет.",
                FontSize = 12.5,
                Foreground = (Brush)new BrushConverter().ConvertFromString("#64748B")!,
                Margin = new Thickness(0, 0, 0, 14)
            });
        }
        else
        {
            foreach (var f in detail.Followups)
            {
                var fCard = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = (Brush)new BrushConverter().ConvertFromString("#E2E8F0")!,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 0, 0, 10)
                };
                var fStack = new StackPanel();
                var fHead = new DockPanel();
                fHead.Children.Add(new TextBlock
                {
                    Text = $"💬 {f.Author}",
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12.5,
                    Foreground = (Brush)new BrushConverter().ConvertFromString("#1E293B")!
                });
                if (f.Date.HasValue)
                {
                    var dtText = new TextBlock
                    {
                        Text = f.Date.Value.ToString("dd.MM.yyyy HH:mm"),
                        FontSize = 11.5,
                        Foreground = (Brush)new BrushConverter().ConvertFromString("#94A3B8")!,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    DockPanel.SetDock(dtText, Dock.Right);
                    fHead.Children.Add(dtText);
                }
                fStack.Children.Add(fHead);
                fStack.Children.Add(new TextBlock
                {
                    Text = f.Content,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12.5,
                    LineHeight = 18,
                    Margin = new Thickness(0, 6, 0, 0)
                });
                fCard.Child = fStack;
                bodyStack.Children.Add(fCard);
            }
        }

        // 5. Input Area for Reply or Solution (only if active)
        if (!isClosed)
        {
            var actionBox = new Border
            {
                Background = Brushes.White,
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 8, 0, 20)
            };
            var abStack = new StackPanel();

            var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var rbReply = new RadioButton { Content = "💬 Добавить ответ (Followup)", IsChecked = true, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 14, 0) };
            var rbSolve = new RadioButton { Content = "✅ Предоставить решение и выполнить заявку (Solve)", FontWeight = FontWeights.SemiBold };
            modeRow.Children.Add(rbReply);
            modeRow.Children.Add(rbSolve);
            abStack.Children.Add(modeRow);

            var inputMsg = new TextBox
            {
                Height = 85,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 13
            };
            abStack.Children.Add(inputMsg);

            var sendBtn = new Button
            {
                Content = "Отправить ответ",
                Background = (Brush)new BrushConverter().ConvertFromString("#2563EB")!,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(16, 7, 16, 7),
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0)
            };

            rbReply.Checked += (_, _) =>
            {
                sendBtn.Content = "Отправить ответ";
                sendBtn.Background = (Brush)new BrushConverter().ConvertFromString("#2563EB")!;
            };
            rbSolve.Checked += (_, _) =>
            {
                sendBtn.Content = "Сохранить решение и перевести в статус «Выполнена»";
                sendBtn.Background = (Brush)new BrushConverter().ConvertFromString("#16A34A")!;
            };

            sendBtn.Click += async (_, _) =>
            {
                string text = inputMsg.Text.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    MessageBox.Show("Пожалуйста, введите текст сообщения.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                sendBtn.IsEnabled = false;
                try
                {
                    if (rbSolve.IsChecked == true)
                    {
                        using var sreq = new HttpRequestMessage(HttpMethod.Post, $"staff-tickets/{ticket.Id}/solve")
                        {
                            Content = JsonContent.Create(new { text })
                        };
                        using var sres = await http.SendAsync(sreq, lifetime.Token);
                        if (!sres.IsSuccessStatusCode) throw new InvalidOperationException("Ошибка сохранения решения: " + sres.StatusCode);
                        MessageBox.Show("Решение успешно добавлено в GLPI. Статус заявки изменён на «Выполнена».", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        using var rreq = new HttpRequestMessage(HttpMethod.Post, $"staff-tickets/{ticket.Id}/reply")
                        {
                            Content = JsonContent.Create(new { text })
                        };
                        using var rres = await http.SendAsync(rreq, lifetime.Token);
                        if (!rres.IsSuccessStatusCode) throw new InvalidOperationException("Ошибка отправки ответа: " + rres.StatusCode);
                        MessageBox.Show("Ответ успешно добавлен в историю заявки GLPI.", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    await LoadOverviewAsync();
                    await OpenTicketCardAsync(ticket.Id, canClaim, isClosed);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    sendBtn.IsEnabled = true;
                }
            };

            abStack.Children.Add(sendBtn);
            actionBox.Child = abStack;
            bodyStack.Children.Add(actionBox);
        }

        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        moduleHost.Content = root;
    }

    private async Task ChangeTicketStatusAsync(int ticketId, int newStatus)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"staff-tickets/{ticketId}/status")
            {
                Content = JsonContent.Create(new { status = newStatus })
            };
            using var res = await http.SendAsync(req, lifetime.Token);
            if (!res.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Не удалось обновить статус: " + res.StatusCode);
            }

            MessageBox.Show("Статус заявки успешно обновлен.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadOverviewAsync();
            await OpenTicketCardAsync(ticketId, canClaim: false, isClosed: newStatus >= 5);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion
}


public sealed record StaffTicketSummary(
    int Id,
    int GlpiId,
    string Title,
    string Description,
    string Username,
    string UserFullName,
    string MachineName,
    int? BranchId,
    string BranchName,
    string Room,
    string Status,
    string StatusName,
    int AssignedGlpiUserId,
    string AssignedUsername,
    DateTime CreatedAt,
    DateTime? SyncedAt,
    DateTime? ClosedAt,
    double? Score,
    string? ScoreComment
);

public sealed record StaffTicketDetail(
    StaffTicketSummary Ticket,
    List<StaffTicketFollowup> Followups,
    List<StaffTicketSolution> Solutions,
    List<StaffTicketAttachmentInfo> Attachments
);

public sealed record StaffTicketFollowup(
    int Id,
    string Author,
    string Content,
    DateTime? Date,
    bool IsPrivate
);

public sealed record StaffTicketSolution(
    int Id,
    string Author,
    string Content,
    DateTime? Date
);

public sealed record StaffTicketAttachmentInfo(
    string Id,
    int TicketId,
    string FileName,
    long FileSize,
    DateTime CreatedAt
);
