using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelHelper;
public sealed record ClientNotice(string Text, string Sender = "", int DurationSeconds = 30, int? ChatPeerId = null);
public sealed record SuperAdminButton(int Id, string Title, string Message, string Target, int DurationSeconds, int OrderIndex, bool IsActive);
public sealed record NoticeComputer(string MachineName, bool IsOnline);
public sealed class SuperAdminWindow : Window
{
    private readonly HttpClient http;
    private readonly CancellationTokenSource lifetime = new();
    private readonly StackPanel panel = new() { Margin = new Thickness(16) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 8) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public SuperAdminWindow(Settings settings)
    {
        Title = "Кнопки супер админа"; Width = 560; Height = 660; MinWidth = 440; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
        Background = Brushes.AliceBlue; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        http = new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(settings.ServerUrl!.TrimEnd('/') + "/api/"), Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.Add("X-Client-Key", settings.ClientToken); http.DefaultRequestHeaders.Add("X-Machine-Name", Environment.MachineName);
        panel.Children.Add(new TextBlock { Text = "Вход SuperAdmin", FontSize = 20, Margin = new Thickness(0, 0, 0, 10) });
        panel.Children.Add(new TextBlock { Text = "Доступ проверяется по роли и привязанному компьютеру. Пароль не сохраняется.", TextWrapping = TextWrapping.Wrap });
        var provider = new ComboBox { Margin = new Thickness(0, 10, 0, 8) }; provider.Items.Add("Active Directory"); provider.Items.Add("Локальная учётная запись"); provider.SelectedIndex = 0; panel.Children.Add(provider);
        panel.Children.Add(new TextBlock { Text = "Логин" }); var login = new TextBox { Text = Environment.UserName, MaxLength = 100 }; panel.Children.Add(login);
        panel.Children.Add(new TextBlock { Text = "Пароль", Margin = new Thickness(0, 10, 0, 0) }); var password = new PasswordBox { MaxLength = 256 }; panel.Children.Add(password);
        var enter = PetWindow.MakeButton("Войти"); enter.Margin = new Thickness(0, 12, 0, 0); panel.Children.Add(enter); panel.Children.Add(status);
        enter.Click += async (_, _) =>
        {
            enter.IsEnabled = false; status.Text = "Проверка доступа…";
            try
            {
                var response = await CallAsync<JsonElement>(provider.SelectedIndex == 0 ? "auth/ad" : "auth/login", HttpMethod.Post, new { username = login.Text, password = password.Password });
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", response.GetProperty("token").GetString()); password.Clear();
                var buttons = await CallAsync<List<SuperAdminButton>>("super-client/buttons", HttpMethod.Get);
                var computers = await CallAsync<List<NoticeComputer>>("super-client/computers", HttpMethod.Get); Editor(buttons, computers);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex) { password.Clear(); http.DefaultRequestHeaders.Authorization = null; status.Text = ex.Message; }
            finally { if (!lifetime.IsCancellationRequested) enter.IsEnabled = true; }
        };
        Closed += (_, _) => { lifetime.Cancel(); http.DefaultRequestHeaders.Authorization = null; http.Dispose(); };
    }
    private async Task<T> CallAsync<T>(string path, HttpMethod method, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path); if (body != null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, lifetime.Token);
        string content = await response.Content.ReadAsStringAsync(lifetime.Token);
        if (!response.IsSuccessStatusCode)
        {
            string error = response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "Неверный логин/пароль или сеанс истёк." : "Доступ запрещён. Нужны роль SuperAdmin и привязка этого ПК в панели.";
            try { using var document = JsonDocument.Parse(content); if (document.RootElement.TryGetProperty("error", out var detail)) error = detail.GetString() ?? error; } catch (JsonException) { }
            throw new InvalidOperationException(error);
        }
        return JsonSerializer.Deserialize<T>(content, Json)!;
    }
    private void Editor(List<SuperAdminButton> buttons, List<NoticeComputer> computers)
    {
        panel.Children.Clear(); status.Text = ""; panel.Children.Add(new TextBlock { Text = "Сообщение помощникам", FontSize = 20 });
        if (buttons.Count == 0) { panel.Children.Add(new TextBlock { Text = "Активные кнопки не настроены. Добавьте их в веб-панели.", TextWrapping = TextWrapping.Wrap }); return; }
        var templates = new WrapPanel { Margin = new Thickness(0, 12, 0, 10) }; panel.Children.Add(new ScrollViewer { Content = templates, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var text = new TextBox { MaxLength = 1000, Height = 120, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; panel.Children.Add(text);
        var all = new CheckBox { Content = "Всем зарегистрированным клиентам", Margin = new Thickness(0, 10, 0, 8) }; panel.Children.Add(all);
        var filter = new TextBox { Margin = new Thickness(0, 0, 0, 6), ToolTip = "Фильтр по имени компьютера" }; panel.Children.Add(filter);
        var targets = new StackPanel(); var checks = computers.Select(c => new CheckBox { Content = c.MachineName + (c.IsOnline ? " · Онлайн" : " · Офлайн"), Tag = c.MachineName, Margin = new Thickness(0, 2, 0, 2) }).ToList();
        foreach (var checkbox in checks) targets.Children.Add(checkbox);
        panel.Children.Add(new ScrollViewer { Content = targets, Height = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        all.Checked += (_, _) => targets.IsEnabled = false; all.Unchecked += (_, _) => targets.IsEnabled = true;
        filter.TextChanged += (_, _) => { foreach (var c in checks) c.Visibility = c.Tag.ToString()!.Contains(filter.Text, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed; };
        panel.Children.Add(new TextBlock { Text = "Время показа (10–300 секунд)", Margin = new Thickness(0, 10, 0, 0) }); var duration = new TextBox { Text = "30", MaxLength = 3 }; panel.Children.Add(duration);
        void Apply(SuperAdminButton b) { text.Text = b.Message; all.IsChecked = b.Target == "all"; duration.Text = b.DurationSeconds.ToString(); }
        foreach (var b in buttons) { var button = PetWindow.MakeButton(b.Title); button.Width = 230; button.Height = 44; button.Margin = new Thickness(0, 0, 6, 6); button.Click += (_, _) => Apply(b); templates.Children.Add(button); }
        Apply(buttons[0]);
        var send = PetWindow.MakeButton("Отправить сообщение"); send.Margin = new Thickness(0, 10, 0, 0); panel.Children.Add(send); panel.Children.Add(status);
        send.Click += async (_, _) =>
        {
            send.IsEnabled = false;
            try
            {
                if (string.IsNullOrWhiteSpace(text.Text)) throw new ArgumentException("Введите сообщение.");
                string[] machines = all.IsChecked == true ? ["ALL"] : checks.Where(c => c.IsChecked == true).Select(c => c.Tag.ToString()!).ToArray();
                if (machines.Length == 0) throw new ArgumentException("Выберите компьютеры.");
                if (!int.TryParse(duration.Text, out int seconds) || seconds is < 10 or > 300) throw new ArgumentException("Время показа: 10–300 секунд.");
                var results = await CallAsync<List<JsonElement>>("super-client/send", HttpMethod.Post, new { machines, text = text.Text, durationSeconds = seconds });
                status.Text = $"Передано: {results.Count(r => r.GetProperty("status").GetString() == "Pending")}; офлайн: {results.Count(r => r.GetProperty("status").GetString() == "Offline")}. Подтверждения клиентов доступны в журнале панели.";
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { if (!lifetime.IsCancellationRequested) send.IsEnabled = true; }
        };
    }
}
