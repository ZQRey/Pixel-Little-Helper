using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace PixelHelper;

/// <summary>
/// Нативное WPF-окно для аудио- и видеозвонков мессенджера PixelHelper
/// с поддержкой полноэкранного режима, WebRTC и записи звонков в папку «Видео».
/// </summary>
public class VideoCallWindow : Window
{
    private readonly string serverAddress;
    private readonly int peerId;
    private readonly string targetName;
    private readonly bool isVideo;
    private readonly bool isCaller;
    private readonly int currentUserId;
    private readonly string? callId;
    private readonly string? token;

    private WebView2? webView;
    private TextBlock? statusText;
    private ProgressBar? loadingBar;
    private Button? fullscreenBtn;
    private bool isFullscreen;

    public VideoCallWindow(string serverAddress, int peerId, string targetName, bool isVideo, bool isCaller, int currentUserId, string? callId = null, string? token = null)
    {
        this.serverAddress = serverAddress;
        this.peerId = peerId;
        this.targetName = string.IsNullOrWhiteSpace(targetName) ? (peerId < 0 ? "Группа" : "Собеседник") : targetName;
        this.isVideo = isVideo;
        this.isCaller = isCaller;
        this.currentUserId = currentUserId;
        this.callId = callId;
        this.token = token;

        Title = (isVideo ? "🎥 Видеозвонок: " : "📞 Аудиозвонок: ") + this.targetName + " · PixelHelper";
        Width = 1060;
        Height = 700;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;
        Background = new SolidColorBrush(Color.FromRgb(11, 15, 25)); // #0B0F19

        try
        {
            var iconUri = new Uri("pack://application:,,,/Assets/PixelHelper.ico");
            Icon = new System.Windows.Media.Imaging.BitmapImage(iconUri);
        }
        catch { }

        KeyDown += OnWindowKeyDown;
        BuildUI();
        Loaded += OnWindowLoaded;
    }

    public static VideoCallWindow OpenCall(string serverAddress, int peerId, string targetName, bool isVideo, bool isCaller, int currentUserId, string? callId = null, string? token = null)
    {
        CallRecordingReceiver.Start();
        var win = new VideoCallWindow(serverAddress, peerId, targetName, isVideo, isCaller, currentUserId, callId, token);
        win.Show();
        win.Activate();
        return win;
    }

    private void BuildUI()
    {
        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) }); // Заголовок
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Контент

        // Верхняя панель управления окном звонка
        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 17, 24, 39)), // #111827
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 0, 14, 0)
        };

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var titleIcon = new TextBlock
        {
            Text = isVideo ? "🎥" : "📞",
            FontSize = 16,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var titleName = new TextBlock
        {
            Text = targetName,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusText = new TextBlock
        {
            Text = " (Инициализация…)",
            Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)),
            FontSize = 13,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        titlePanel.Children.Add(titleIcon);
        titlePanel.Children.Add(titleName);
        titlePanel.Children.Add(statusText);
        Grid.SetColumn(titlePanel, 0);
        headerGrid.Children.Add(titlePanel);

        var actionsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        fullscreenBtn = new Button
        {
            Content = "⛶ На весь экран",
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        fullscreenBtn.Click += (_, _) => ToggleFullscreen();

        actionsPanel.Children.Add(fullscreenBtn);
        Grid.SetColumn(actionsPanel, 1);
        headerGrid.Children.Add(actionsPanel);

        header.Child = headerGrid;
        Grid.SetRow(header, 0);
        mainGrid.Children.Add(header);

        // Область WebView2 и индикатор загрузки
        var contentGrid = new Grid();
        Grid.SetRow(contentGrid, 1);

        loadingBar = new ProgressBar
        {
            IsIndeterminate = true,
            Height = 4,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(59, 130, 246)) // #3B82F6
        };

        webView = new WebView2();
        contentGrid.Children.Add(webView);
        contentGrid.Children.Add(loadingBar);

        mainGrid.Children.Add(contentGrid);
        Content = mainGrid;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (webView == null) return;

        try
        {
            string profileDir = Path.Combine(Path.GetTempPath(), "PixelHelper", "WebView2_Call");
            Directory.CreateDirectory(profileDir);

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: profileDir);
            await webView.EnsureCoreWebView2Async(env);

            if (statusText != null) statusText.Text = " (Вызов…)";
            if (loadingBar != null) loadingBar.Visibility = Visibility.Collapsed;

            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.IsZoomControlEnabled = false;

            // Автоматическое разрешение доступа к микрофону и камере без раздражающих всплывающих окон
            webView.CoreWebView2.PermissionRequested += (_, args) =>
            {
                if (args.PermissionKind == CoreWebView2PermissionKind.Camera ||
                    args.PermissionKind == CoreWebView2PermissionKind.Microphone ||
                    args.PermissionKind == CoreWebView2PermissionKind.Notifications)
                {
                    args.State = Uri.TryCreate(args.Uri, UriKind.Absolute, out var origin) &&
                        Uri.Compare(origin, new Uri(CallServerAddress.Secure(serverAddress)), UriComponents.SchemeAndServer,
                            UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0
                        ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
                }
            };

            // При вызове window.close() из веб-интерфейса закрываем окно WPF
            webView.CoreWebView2.WindowCloseRequested += (_, _) =>
            {
                Dispatcher.Invoke(Close);
            };

            string host = CallServerAddress.Secure(serverAddress);
            string url = $"{host}/messenger/call.html?peer={peerId}&video={(isVideo ? 1 : 0)}&caller={(isCaller ? 1 : 0)}&user={currentUserId}&name={Uri.EscapeDataString(targetName)}&port={CallRecordingReceiver.LocalPort}&v=1.2.69";
            if (!string.IsNullOrEmpty(callId))
            {
                url += $"&callId={Uri.EscapeDataString(callId)}";
            }
            if (!string.IsNullOrEmpty(token))
            {
                url += $"&token={Uri.EscapeDataString(token)}";
            }

            // Установка куки авторизации в профиле WebView2
            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    var targetUri = new Uri(host);
                    var cm = webView.CoreWebView2.CookieManager;
                    var cookie = cm.CreateCookie("__Host-HelperMessenger", token, targetUri.Host, "/");
                    cookie.IsSecure = targetUri.Scheme == "https";
                    cookie.IsHttpOnly = true;
                    cm.AddOrUpdateCookie(cookie);

                    var altCookie = cm.CreateCookie("HelperMessenger", token, targetUri.Host, "/");
                    altCookie.IsSecure = targetUri.Scheme == "https";
                    cm.AddOrUpdateCookie(altCookie);
                }
                catch (Exception cookieEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[VideoCallWindow] CookieManager: {cookieEx.Message}");
                }
            }

            webView.CoreWebView2.Navigate(url);
        }
        catch (Exception ex)
        {
            if (statusText != null) statusText.Text = " (Ошибка инициализации: " + ex.Message + ")";
            if (loadingBar != null) loadingBar.Visibility = Visibility.Collapsed;

            // Fallback: запуск автономного окна браузера через VideoCallLauncher
            VideoCallLauncher.LaunchFallback(serverAddress, peerId, targetName, isVideo, isCaller, currentUserId, callId, token);
            Close();
        }
    }

    private void ToggleFullscreen()
    {
        if (!isFullscreen)
        {
            WindowState = WindowState.Maximized;
            if (fullscreenBtn != null) fullscreenBtn.Content = "❐ Свернуть в окно";
            isFullscreen = true;
        }
        else
        {
            WindowState = WindowState.Normal;
            if (fullscreenBtn != null) fullscreenBtn.Content = "⛶ На весь экран";
            isFullscreen = false;
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        try
        {
            if (webView?.CoreWebView2 != null)
            {
                // Уведомляем собеседника о завершении вызова перед закрытием
                await webView.ExecuteScriptAsync("if (typeof hangupCall === 'function') hangupCall();");
            }
        }
        catch { }
        finally
        {
            webView?.Dispose();
        }
    }
}
