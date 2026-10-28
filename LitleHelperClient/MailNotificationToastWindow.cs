using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PixelHelper;

internal sealed class MailNotificationToastWindow : Window
{
    private readonly DispatcherTimer closeTimer;

    internal MailNotificationToastWindow(
        MailMessageItem message,
        bool showPreview,
        Action onOpenMail,
        Action onMarkRead)
    {
        Title = "Новое письмо — Pixel Mail";
        Width = 370;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;

        bool isWarning = message.SecurityVerdict == MailSecurityVerdict.PhishingWarning;
        var accentBrush = new SolidColorBrush(isWarning ? Color.FromRgb(217, 119, 6) : Color.FromRgb(37, 99, 235));
        var cardBg = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        var borderBrush = new SolidColorBrush(isWarning ? Color.FromRgb(245, 158, 11) : Color.FromRgb(59, 130, 246));

        var card = new Border
        {
            Background = cardBg,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(8),
            Padding = new Thickness(14),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 3,
                Opacity = 0.45,
                Color = Colors.Black
            }
        };

        var root = new StackPanel();

        // Header row
        var headerDock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var closeBtn = new Button
        {
            Content = "✕",
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontSize = 12,
            Padding = new Thickness(4, 0, 4, 0)
        };
        closeBtn.Click += (_, _) => Close();
        DockPanel.SetDock(closeBtn, Dock.Right);
        headerDock.Children.Add(closeBtn);

        var headerText = new TextBlock
        {
            Text = $"✉️ Новое письмо · {message.AccountEmail}",
            Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250)),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        headerDock.Children.Add(headerText);
        root.Children.Add(headerDock);

        // Sender
        root.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(message.SenderDisplay) ? "Неизвестный отправитель" : message.SenderDisplay,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 3)
        });

        // Subject & Snippet
        if (showPreview)
        {
            root.Children.Add(new TextBlock
            {
                Text = message.Subject,
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                FontWeight = FontWeights.Medium,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 38,
                Margin = new Thickness(0, 0, 0, 4)
            });

            if (!string.IsNullOrWhiteSpace(message.Snippet))
            {
                root.Children.Add(new TextBlock
                {
                    Text = message.Snippet,
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    FontSize = 11.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 0, 6)
                });
            }
        }
        else
        {
            root.Children.Add(new TextBlock
            {
                Text = "Получено новое электронное письмо",
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            });
        }

        // Security badge
        var badgeBorder = new Border
        {
            Background = new SolidColorBrush(isWarning ? Color.FromArgb(40, 245, 158, 11) : Color.FromArgb(40, 34, 197, 94)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        badgeBorder.Child = new TextBlock
        {
            Text = isWarning ? "⚠️ Подозрение на фишинг (PixelGuard AV)" : "🛡️ Проверено PixelGuard AV",
            Foreground = new SolidColorBrush(isWarning ? Color.FromRgb(251, 191, 36) : Color.FromRgb(74, 222, 128)),
            FontSize = 11,
            FontWeight = FontWeights.Medium
        };
        root.Children.Add(badgeBorder);

        // Action buttons
        var actionsPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var markReadBtn = new Button
        {
            Content = "✓ Прочитано",
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontSize = 12
        };
        markReadBtn.Click += (_, _) =>
        {
            onMarkRead();
            Close();
        };

        var openBtn = new Button
        {
            Content = "📖 Открыть письмо",
            Background = accentBrush,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 5, 12, 5),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12
        };
        openBtn.Click += (_, _) =>
        {
            onOpenMail();
            Close();
        };

        actionsPanel.Children.Add(markReadBtn);
        actionsPanel.Children.Add(openBtn);
        root.Children.Add(actionsPanel);

        card.Child = root;
        Content = card;

        Loaded += (_, _) =>
        {
            var workArea = SystemParameters.WorkArea;
            Left = Math.Max(workArea.Left + 10, workArea.Right - ActualWidth - 16);
            Top = Math.Max(workArea.Top + 10, workArea.Bottom - ActualHeight - 16);
        };

        closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        closeTimer.Tick += (_, _) =>
        {
            closeTimer.Stop();
            Close();
        };
        closeTimer.Start();
        Closed += (_, _) => closeTimer.Stop();
    }
}

