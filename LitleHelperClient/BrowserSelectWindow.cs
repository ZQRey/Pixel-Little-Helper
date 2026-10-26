using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelHelper;

public sealed class BrowserSelectWindow : Window
{
    private readonly Settings settings;
    private readonly List<InstalledBrowser> browsers;
    private InstalledBrowser? selectedBrowser;
    private readonly StackPanel listPanel;
    private readonly Button saveBtn;
    private readonly Dictionary<InstalledBrowser, (Border Card, TextBlock Indicator)> cardViews = new();

    public BrowserSelectWindow(Settings settings)
    {
        this.settings = settings;

        Title = "Выбор основного браузера";
        Width = 480;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = true;
        Background = Brushes.Transparent;
        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;

        var rootBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)), // Slate 600
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), // Slate 900
            Padding = new Thickness(20)
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Description
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // List
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer buttons

        // Header bar with drag support
        var header = new Grid { Margin = new Thickness(0, 0, 0, 10), Background = Brushes.Transparent };
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        var titleBlock = new TextBlock
        {
            Text = "🌐 Выбор основного браузера",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)) // Slate 100
        };

        var closeBtn = new Button
        {
            Content = "✕",
            Width = 28,
            Height = 28,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = Cursors.Hand
        };
        closeBtn.Click += (_, _) => Close();

        header.Children.Add(titleBlock);
        header.Children.Add(closeBtn);
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        // Description
        var descBlock = new TextBlock
        {
            Text = "PixelHelper проверяет ссылки из Telegram и почты на мошенничество. Выберите, в каком браузере открывать проверенные безопасные сайты:",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)), // Slate 300
            Margin = new Thickness(0, 0, 0, 14)
        };
        Grid.SetRow(descBlock, 1);
        layout.Children.Add(descBlock);

        // Browser List in ScrollViewer
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 0, 14)
        };

        listPanel = new StackPanel();
        browsers = BrowserDetector.GetInstalledBrowsers();

        // Find initial selection
        if (!string.IsNullOrWhiteSpace(settings.PreferredBrowserPath))
        {
            selectedBrowser = browsers.FirstOrDefault(b => string.Equals(b.ExecutablePath, settings.PreferredBrowserPath, StringComparison.OrdinalIgnoreCase));
        }
        selectedBrowser ??= browsers.FirstOrDefault();

        foreach (var browser in browsers)
        {
            var card = CreateBrowserCard(browser);
            listPanel.Children.Add(card);
        }

        if (browsers.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = "В системе не обнаружено стандартных браузеров. Будет использоваться системный обработчик по умолчанию.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8)
            };
            listPanel.Children.Add(emptyText);
        }

        scroll.Content = listPanel;
        Grid.SetRow(scroll, 2);
        layout.Children.Add(scroll);

        // Footer buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        var cancelBtn = new Button
        {
            Content = "Отмена",
            Height = 34,
            Padding = new Thickness(16, 0, 16, 0),
            Margin = new Thickness(0, 0, 8, 0),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        cancelBtn.Click += (_, _) => Close();

        saveBtn = new Button
        {
            Content = "Сохранить выбор",
            Height = 34,
            Padding = new Thickness(20, 0, 20, 0),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)), // Blue 600
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            IsEnabled = selectedBrowser != null
        };
        saveBtn.Click += (_, _) => SaveAndClose();

        buttonPanel.Children.Add(cancelBtn);
        buttonPanel.Children.Add(saveBtn);
        Grid.SetRow(buttonPanel, 3);
        layout.Children.Add(buttonPanel);

        rootBorder.Child = layout;
        Content = rootBorder;

        UpdateCardStyles();
    }

    private Border CreateBrowserCard(InstalledBrowser browser)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 8),
            Cursor = Cursors.Hand
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        // Icon
        var iconSource = TryGetBrowserIcon(browser.ExecutablePath);
        UIElement iconElement;
        if (iconSource != null)
        {
            iconElement = new Image
            {
                Source = iconSource,
                Width = 24,
                Height = 24,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
        }
        else
        {
            iconElement = new TextBlock
            {
                Text = "🌐",
                FontSize = 20,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        Grid.SetColumn(iconElement, 0);
        row.Children.Add(iconElement);

        // Details
        var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
        var nameText = new TextBlock
        {
            Text = browser.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White
        };
        var pathText = new TextBlock
        {
            Text = browser.ExecutablePath,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), // Slate 400
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        details.Children.Add(nameText);
        details.Children.Add(pathText);
        Grid.SetColumn(details, 1);
        row.Children.Add(details);

        // Radio Indicator
        var indicator = new TextBlock
        {
            Text = "○",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(indicator, 2);
        row.Children.Add(indicator);

        card.Child = row;
        cardViews[browser] = (card, indicator);

        card.MouseLeftButtonDown += (_, e) =>
        {
            selectedBrowser = browser;
            saveBtn.IsEnabled = true;
            UpdateCardStyles();
            if (e.ClickCount == 2)
            {
                SaveAndClose();
            }
        };

        return card;
    }

    private void UpdateCardStyles()
    {
        foreach (var (b, (card, indicator)) in cardViews)
        {
            bool isSelected = selectedBrowser != null &&
                              string.Equals(selectedBrowser.ExecutablePath, b.ExecutablePath, StringComparison.OrdinalIgnoreCase);

            if (isSelected)
            {
                card.Background = new SolidColorBrush(Color.FromRgb(30, 58, 138)); // Blue 900
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246)); // Blue 500
                indicator.Text = "●";
                indicator.Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250)); // Blue 400
            }
            else
            {
                card.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)); // Slate 800
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)); // Slate 700
                indicator.Text = "○";
                indicator.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)); // Slate 500
            }
        }
    }

    private void SaveAndClose()
    {
        if (selectedBrowser != null)
        {
            settings.PreferredBrowserPath = selectedBrowser.ExecutablePath;
            settings.PreferredBrowserName = selectedBrowser.Name;
        }
        settings.BrowserPromptShown = true;
        settings.Save();
        Close();
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern uint ExtractIconEx(string szFileName, int nIconIndex, out nint phiconLarge, out nint phiconSmall, uint nIcons);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);

    private static ImageSource? TryGetBrowserIcon(string exePath)
    {
        try
        {
            if (File.Exists(exePath))
            {
                if (ExtractIconEx(exePath, 0, out nint hLarge, out nint hSmall, 1) > 0)
                {
                    nint chosen = hSmall != nint.Zero ? hSmall : hLarge;
                    ImageSource? result = null;
                    if (chosen != nint.Zero)
                    {
                        result = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            chosen,
                            Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    }
                    if (hSmall != nint.Zero) DestroyIcon(hSmall);
                    if (hLarge != nint.Zero) DestroyIcon(hLarge);
                    return result;
                }
            }
        }
        catch { }
        return null;
    }
}
