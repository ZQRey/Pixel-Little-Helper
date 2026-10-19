using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace PixelHelper;

/// <summary>
/// Всплывающее нативное окно входящего аудио- или видеозвонка.
/// Отображается поверх всех окон (Topmost), воспроизводит мелодию вызова,
/// позволяет принять или отклонить вызов с автоматическим таймаутом.
/// </summary>
internal sealed class IncomingCallWindow : Window
{
    private static IncomingCallWindow? activeWindow;
    private static readonly object sync = new();

    private readonly MessengerClient client;
    private readonly Settings settings;
    private readonly string callId;
    private readonly int callerId;
    private readonly string callerName;
    private readonly bool isVideo;

    private readonly DispatcherTimer ringTimer = new();
    private readonly DispatcherTimer timeoutTimer = new();
    private bool handled;

    internal IncomingCallWindow(MessengerClient client, Settings settings, string callId, int callerId, string callerName, bool isVideo)
    {
        this.client = client;
        this.settings = settings;
        this.callId = callId;
        this.callerId = callerId;
        this.callerName = string.IsNullOrWhiteSpace(callerName) ? "Собеседник" : callerName;
        this.isVideo = isVideo;

        Title = (isVideo ? "🎥 Входящий видеозвонок" : "📞 Входящий аудиозвонок") + " · PixelHelper";
        Width = 420;
        Height = 290;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = true;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        try
        {
            var iconUri = new Uri("pack://application:,,,/Assets/PixelHelper.ico");
            Icon = new System.Windows.Media.Imaging.BitmapImage(iconUri);
        }
        catch { }

        Content = BuildUI();

        KeyDown += OnKeyDown;
        Loaded += OnLoaded;
        Closing += (_, _) => Cleanup();
    }

    internal static void ShowCall(MessengerClient client, Settings settings, string callId, int callerId, string callerName, bool isVideo)
    {
        void Action()
        {
            lock (sync)
            {
                if (activeWindow != null && activeWindow.IsLoaded)
                {
                    try
                    {
                        activeWindow.Activate();
                        activeWindow.Focus();
                    }
                    catch { }
                    return;
                }

                var win = new IncomingCallWindow(client, settings, callId, callerId, callerName, isVideo);
                activeWindow = win;
                win.Closed += (_, _) =>
                {
                    lock (sync)
                    {
                        if (activeWindow == win) activeWindow = null;
                    }
                };
                win.Show();
                win.Activate();
                win.Focus();
            }
        }

        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.BeginInvoke((Action)Action);
        }
        else
        {
            Action();
        }
    }

    private UIElement BuildUI()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), // Slate 900 (#0F172A)
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 99, 102, 241)), // Indigo border
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(24, 20, 24, 22),
            Effect = new DropShadowEffect
            {
                BlurRadius = 36,
                ShadowDepth = 8,
                Opacity = 0.55,
                Color = Colors.Black
            }
        };

        border.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };

        var mainPanel = new Grid();
        mainPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Шапка
        mainPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Инфо о звонящем
        mainPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Кнопки

        // 1. Верхняя панель (иконка + бренд + статус)
        var topBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var brandLabel = new TextBlock
        {
            Text = "PixelHelper · " + (isVideo ? "Входящий видеозвонок" : "Входящий аудиозвонок"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)) // Slate 400
        };
        topBar.Children.Add(brandLabel);
        Grid.SetRow(topBar, 0);
        mainPanel.Children.Add(topBar);

        // 2. Центральная панель: круглая иконка + имя звонящего
        var centerPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 16)
        };

        var iconContainer = new Border
        {
            Width = 64,
            Height = 64,
            CornerRadius = new CornerRadius(32),
            Background = new SolidColorBrush(isVideo ? Color.FromArgb(40, 59, 130, 246) : Color.FromArgb(40, 16, 185, 129)),
            BorderBrush = new SolidColorBrush(isVideo ? Color.FromRgb(59, 130, 246) : Color.FromRgb(16, 185, 129)),
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12),
            Child = new TextBlock
            {
                Text = isVideo ? "🎥" : "📞",
                FontSize = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var nameText = new TextBlock
        {
            Text = callerName,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 360
        };

        var promptText = new TextBlock
        {
            Text = isVideo ? "Вызывает по видеосвязи…" : "Вызывает по аудиосвязи…",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)), // Slate 300
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };

        centerPanel.Children.Add(iconContainer);
        centerPanel.Children.Add(nameText);
        centerPanel.Children.Add(promptText);
        Grid.SetRow(centerPanel, 1);
        mainPanel.Children.Add(centerPanel);

        // 3. Нижняя панель: кнопки «Отклонить» и «Принять»
        var buttonsGrid = new Grid();
        buttonsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttonsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        buttonsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var declineBtn = CreateActionButton("📞✕ Отклонить", Color.FromRgb(239, 68, 68), Color.FromRgb(220, 38, 38), (_, _) => Reject());
        Grid.SetColumn(declineBtn, 0);
        buttonsGrid.Children.Add(declineBtn);

        var acceptBtn = CreateActionButton(isVideo ? "🎥 Принять" : "📞 Принять", Color.FromRgb(16, 185, 129), Color.FromRgb(5, 150, 105), (_, _) => Accept());
        Grid.SetColumn(acceptBtn, 2);
        buttonsGrid.Children.Add(acceptBtn);

        Grid.SetRow(buttonsGrid, 2);
        mainPanel.Children.Add(buttonsGrid);

        border.Child = mainPanel;
        return border;
    }

    private static Button CreateActionButton(string text, Color baseColor, Color hoverColor, RoutedEventHandler click)
    {
        var btn = new Button
        {
            Content = text,
            Background = new SolidColorBrush(baseColor),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Height = 44,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };

        var template = new ControlTemplate(typeof(Button));
        var elemFactory = new FrameworkElementFactory(typeof(Border));
        elemFactory.Name = "border";
        elemFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        elemFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        elemFactory.SetValue(Border.PaddingProperty, new Thickness(14, 8, 14, 8));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        elemFactory.AppendChild(presenter);

        var trigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        trigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(hoverColor), "border"));

        var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(hoverColor), "border"));

        template.VisualTree = elemFactory;
        template.Triggers.Add(trigger);
        template.Triggers.Add(pressedTrigger);
        btn.Template = template;

        btn.Click += click;
        return btn;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Activate();
        Focus();
        Topmost = true;

        // Первый сигнал вызова
        PlayRing();

        // Повторяем звонок каждые 3.2 сек
        ringTimer.Interval = TimeSpan.FromSeconds(3.2);
        ringTimer.Tick += (_, _) => PlayRing();
        ringTimer.Start();

        // Авто-отклонение через 35 секунд если никто не ответил
        timeoutTimer.Interval = TimeSpan.FromSeconds(35);
        timeoutTimer.Tick += (_, _) =>
        {
            timeoutTimer.Stop();
            Reject();
        };
        timeoutTimer.Start();
    }

    private void PlayRing()
    {
        try
        {
            SoundManager.Play(SoundEvent.Urgent, settings);
        }
        catch
        {
            try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
        }
    }

    private void Accept()
    {
        if (handled) return;
        handled = true;
        Cleanup();
        Close();

        try
        {
            VideoCallWindow.OpenCall(client.ServerAddress, callerId, callerName, isVideo, isCaller: false, client.UserId, callId, client.Token);
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    private void Reject()
    {
        if (handled) return;
        handled = true;
        Cleanup();
        Close();

        try
        {
            _ = client.RejectCallAsync(callId, callerId);
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    private void Cleanup()
    {
        try { ringTimer.Stop(); } catch { }
        try { timeoutTimer.Stop(); } catch { }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Reject();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter || e.Key == Key.Space)
        {
            Accept();
            e.Handled = true;
        }
    }
}
