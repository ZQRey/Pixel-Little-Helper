using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PixelHelper;

public sealed record DiagnosticCheckItem(string Title, string Category, bool Success, string Details, bool IsWarning = false);

internal sealed class DiagnosticWindow : Window
{
    private readonly Settings settings;
    private readonly HubConnectionService? hub;
    private readonly Action<string>? openTicketWithText;
    private readonly StackPanel itemsPanel = new();
    private readonly TextBlock statusText = new();
    private readonly Button refreshBtn;
    private readonly Button ticketBtn;
    private readonly Button cleanBtn;
    private readonly List<DiagnosticCheckItem> lastResults = new();

    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    public DiagnosticWindow(Settings settings, Action<string>? openTicketWithText = null)
        : this(settings, null, openTicketWithText)
    {
    }

    public DiagnosticWindow(Settings settings, HubConnectionService? hub, Action<string>? openTicketWithText = null)
    {
        this.settings = settings;
        this.hub = hub;
        this.openTicketWithText = openTicketWithText;

        Title = "Экспресс-диагностика рабочего места";
        Width = 520; Height = 600;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };

        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E2E8F0"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(10),
            Effect = new DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 6,
                Opacity = 0.20,
                Color = Color.FromRgb(15, 23, 42)
            }
        };

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer buttons

        // 1. Header
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var closeBtn = new Button
        {
            Content = "×",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#64748B"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Width = 28,
            Height = 28,
            Cursor = System.Windows.Input.Cursors.Hand,
            Padding = new Thickness(0, -2, 0, 0)
        };
        closeBtn.Click += (_, _) => Close();
        DockPanel.SetDock(closeBtn, Dock.Right);
        header.Children.Add(closeBtn);

        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = "🩺 Экспресс-диагностика рабочего места",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#0F172A")
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Автоматическая проверка связи, сервисов, диска и принтеров",
            FontSize = 11,
            Foreground = Brush("#64748B"),
            Margin = new Thickness(0, 2, 0, 0)
        });
        header.Children.Add(titleStack);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // 2. Scrollable check items
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 10) };
        scroll.Content = itemsPanel;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        // 3. Footer
        var footer = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        statusText.Text = "Идёт сканирование систем…";
        statusText.FontSize = 11;
        statusText.Foreground = Brush("#64748B");
        statusText.Margin = new Thickness(0, 0, 0, 8);
        footer.Children.Add(statusText);

        var btnBar = new DockPanel();
        refreshBtn = new Button
        {
            Content = "🔄 Повторить",
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Background = Brush("#F1F5F9"),
            BorderBrush = Brush("#CBD5E1"),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0)
        };
        refreshBtn.Click += async (_, _) => await RunDiagnosticsAsync();
        btnBar.Children.Add(refreshBtn);

        cleanBtn = new Button
        {
            Content = "🧹 Очистить мусор",
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Background = Brush("#F1F5F9"),
            BorderBrush = Brush("#CBD5E1"),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0)
        };
        cleanBtn.Click += async (_, _) =>
        {
            cleanBtn.IsEnabled = false;
            statusText.Text = "Очистка временных файлов…";
            var res = await DiskCleaner.CleanAsync();
            statusText.Text = res.Message;
            cleanBtn.IsEnabled = true;
            await RunDiagnosticsAsync();
        };
        btnBar.Children.Add(cleanBtn);

        ticketBtn = new Button
        {
            Content = "🎫 Заявка с логом",
            Padding = new Thickness(14, 6, 14, 6),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Background = Brush("#4F46E5"),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(ticketBtn, Dock.Right);
        ticketBtn.Click += (_, _) =>
        {
            string summary = GenerateReport();
            openTicketWithText?.Invoke(summary);
            Close();
        };
        btnBar.Children.Add(ticketBtn);

        footer.Children.Add(btnBar);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        card.Child = root;
        Content = card;

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            NativeMethods.ToolWindow(handle, false);
            NativeMethods.SetWindowPos(handle, new nint(-1), 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
        };

        Loaded += async (_, _) =>
        {
            Topmost = true;
            Activate();
            await RunDiagnosticsAsync();
        };
    }

    private async Task RunDiagnosticsAsync()
    {
        refreshBtn.IsEnabled = false;
        itemsPanel.Children.Clear();
        lastResults.Clear();
        statusText.Text = "Тестирование сети, NCALayer, диска и сервисов…";

        var checks = await Task.Run(async () =>
        {
            var results = new List<DiagnosticCheckItem>();

            // 1. Сеть и шлюз
            bool netOk = false;
            string netDetails = "";
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync("8.8.8.8", 1500);
                netOk = reply.Status == IPStatus.Success;
                netDetails = netOk ? $"Связь стабильна, задержка {reply.RoundtripTime} мс" : "Шлюз/Интернет не отвечает";
            }
            catch (Exception ex)
            {
                netDetails = "Сетевой адаптер отключен или нет доступа: " + ex.Message;
            }
            results.Add(new("Интернет и локальная сеть", "Сеть", netOk, netDetails));

            // 2. Сервер Помощника
            bool serverOk = false;
            string serverDetails = "";
            bool isSignalrOnline = hub?.IsOnline == true;

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                string baseUrl = (settings.ServerUrl ?? "http://helper.gp1.loc").TrimEnd('/');
                
                // Проверяем /health (основной) или /api/health (дополнительный)
                var res = await client.GetAsync(baseUrl + "/health");
                if (!res.IsSuccessStatusCode)
                {
                    res = await client.GetAsync(baseUrl + "/api/health");
                }

                if (res.IsSuccessStatusCode)
                {
                    serverOk = true;
                    serverDetails = isSignalrOnline 
                        ? "Сервер доступен (SignalR подключен, HTTP 200 OK)" 
                        : "Сервер доступен (HTTP 200 OK)";
                }
                else
                {
                    if (isSignalrOnline)
                    {
                        serverOk = true;
                        serverDetails = "Связь с сервером активна (SignalR подключен)";
                    }
                    else
                    {
                        serverDetails = $"Код ответа: {(int)res.StatusCode}";
                    }
                }
            }
            catch (Exception ex)
            {
                if (isSignalrOnline)
                {
                    serverOk = true;
                    serverDetails = "Связь с сервером активна (SignalR подключен)";
                }
                else
                {
                    serverDetails = "Сервер помощника недоступен: " + ex.Message;
                }
            }
            results.Add(new("Сервер LitleHelper", "Инфраструктура", serverOk, serverDetails));

            // 3. Медицинская система DMED
            bool dmedOk = false;
            string dmedDetails = "";
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
                var res = await client.GetAsync("https://krg.dmed.kz");
                dmedOk = res.IsSuccessStatusCode;
                dmedDetails = dmedOk ? "Портал DMED доступен" : $"Код ответа: {(int)res.StatusCode}";
            }
            catch (Exception ex)
            {
                dmedDetails = "Не удалось подключиться: " + ex.Message;
            }
            results.Add(new("Портал DMED (krg.dmed.kz)", "Медицина", dmedOk, dmedDetails));

            // 4. Медицинская система ЕИСЗ
            bool eiszOk = false;
            string eiszDetails = "";
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
                var res = await client.GetAsync("https://www.eisz.kz");
                eiszOk = res.IsSuccessStatusCode;
                eiszDetails = eiszOk ? "Портал ЕИСЗ доступен" : $"Код ответа: {(int)res.StatusCode}";
            }
            catch (Exception ex)
            {
                eiszDetails = "Портал недоступен: " + ex.Message;
            }
            results.Add(new("Портал ЕИСЗ (eisz.kz)", "Медицина", eiszOk, eiszDetails));

            // 5. NCALayer (процесс и сокет 13579)
            bool ncaRunning = Process.GetProcessesByName("ncalayer").Length > 0;
            bool ncaPort = false;
            if (ncaRunning)
            {
                try
                {
                    using var tcp = new TcpClient();
                    var connectTask = tcp.ConnectAsync("127.0.0.1", 13579);
                    await Task.WhenAny(connectTask, Task.Delay(1500));
                    ncaPort = tcp.Connected;
                }
                catch { }
            }
            bool ncaSuccess = ncaRunning && ncaPort;
            string ncaDetails = ncaSuccess
                ? "NCALayer активен, порт 13579 отвечает"
                : (!ncaRunning ? "Процесс ncalayer.exe не запущен" : "Процесс запущен, но порт 13579 заблокирован");
            results.Add(new("NCALayer (ЭЦП)", "Безопасность", ncaSuccess, ncaDetails));

            // 6. Дисковое пространство (C:)
            double? freeGb = null;
            double? freePercent = null;
            bool diskOk = true;
            bool diskWarning = false;
            string diskDetails = "";
            try
            {
                var drive = new DriveInfo("C");
                if (drive.IsReady)
                {
                    freeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    freePercent = (drive.AvailableFreeSpace * 100.0) / drive.TotalSize;
                    if (freeGb < 3.0) { diskOk = false; }
                    else if (freeGb < 8.0) { diskWarning = true; }
                    diskDetails = $"Свободно {freeGb:F1} ГБ ({freePercent:F1}%)";
                }
            }
            catch { diskDetails = "Ошибка чтения диска"; diskOk = false; }
            results.Add(new("Свободное место на диске C:", "Хранилище", diskOk, diskDetails, diskWarning));

            // 7. Принтер по умолчанию
            var printers = PrinterWatchdog.GetPrinters();
            var defPrinter = printers.FirstOrDefault(p => p.IsDefault) ?? printers.FirstOrDefault();
            bool printerOk = defPrinter != null && !defPrinter.HasError && defPrinter.JobCount == 0;
            string printDetails = defPrinter == null
                ? "В системе не настроен ни один принтер"
                : $"Принтер: \"{defPrinter.Name}\" ({defPrinter.StatusText})";
            results.Add(new("Принтер по умолчанию", "Печать", printerOk, printDetails, defPrinter != null && defPrinter.JobCount > 0));

            return results;
        });

        lastResults.AddRange(checks);

        foreach (var item in checks)
        {
            itemsPanel.Children.Add(RenderCheckRow(item));
        }

        int passed = checks.Count(c => c.Success && !c.IsWarning);
        int total = checks.Count;
        statusText.Text = passed == total
            ? "✓ Все системы работают отлично! Готово к приёму пациентов."
            : $"Обнаружены замечания в {total - passed} компонентах. Рекомендуется отправить заявку в ИТ.";
        refreshBtn.IsEnabled = true;
    }

    private UIElement RenderCheckRow(DiagnosticCheckItem item)
    {
        var row = new Border
        {
            Background = Brush(item.Success ? (item.IsWarning ? "#FFFBEB" : "#F8FAFC") : "#FEF2F2"),
            BorderBrush = Brush(item.Success ? (item.IsWarning ? "#FDE68A" : "#E2E8F0") : "#FECACA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6)
        };

        var dock = new DockPanel();
        var icon = new TextBlock
        {
            Text = item.Success ? (item.IsWarning ? "⚠️" : "✓") : "✗",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(item.Success ? (item.IsWarning ? "#D97706" : "#16A34A") : "#DC2626"),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);

        var textStack = new StackPanel();
        textStack.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#0F172A")
        });
        textStack.Children.Add(new TextBlock
        {
            Text = item.Details,
            FontSize = 11,
            Foreground = Brush("#64748B"),
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        dock.Children.Add(textStack);

        row.Child = dock;
        return row;
    }

    private string GenerateReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Отчёт экспресс-диагностики рабочего места:");
        sb.AppendLine($"Время: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
        sb.AppendLine($"ПК: {Environment.MachineName}, Пользователь: {SystemInspector.GetUserFullName()} ({Environment.UserName})");
        sb.AppendLine("----------------------------------------");
        foreach (var item in lastResults)
        {
            string mark = item.Success ? (item.IsWarning ? "[ТРЕБУЕТ ВНИМАНИЯ]" : "[OK]") : "[ОШИБКА]";
            sb.AppendLine($"{mark} {item.Title}: {item.Details}");
        }
        sb.AppendLine("----------------------------------------");
        sb.AppendLine("Просьба проверить указанные ошибки.");
        return sb.ToString();
    }
}
