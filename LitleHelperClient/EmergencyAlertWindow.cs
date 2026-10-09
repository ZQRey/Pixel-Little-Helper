using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PixelHelper;

public sealed class EmergencyAlertWindow : Window
{
    private readonly EmergencyAlertNotice notice;
    private readonly Action<bool>? onResponse;
    private readonly bool isTest;
    private readonly Action? onCancel;
    private readonly Sprites sprites = new();
    private readonly Image robotImage = new();
    private readonly DispatcherTimer timer = new();
    private readonly DispatcherTimer animTimer = new();
    private int remainingSeconds;
    private int spriteFrame;
    private TextBlock? timerBlock;
    private PetState petState = PetState.Notice;

    public EmergencyAlertWindow(EmergencyAlertNotice notice, Action<bool>? onResponse = null, bool isTest = false, Action? onCancel = null)
    {
        this.notice = notice;
        this.onResponse = onResponse;
        this.isTest = isTest;
        this.onCancel = onCancel;

        if (notice.Code.Equals("CODE_PINK", StringComparison.OrdinalIgnoreCase))
        {
            remainingSeconds = isTest ? Math.Min(60, notice.DurationSeconds > 0 ? notice.DurationSeconds : 60) : Math.Max(1200, notice.DurationSeconds);
        }
        else
        {
            remainingSeconds = isTest ? Math.Min(60, notice.DurationSeconds > 0 ? notice.DurationSeconds : 45) : Math.Clamp(notice.DurationSeconds > 0 ? notice.DurationSeconds : 300, 60, 600);
        }

        WindowStyle = WindowStyle.None;
        WindowState = WindowState.Maximized;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;

        BuildUi();
        StartTimers();
    }

    private Color GetThemeColor() => notice.Code.ToUpperInvariant() switch
    {
        "CODE_RED" => Color.FromArgb(240, 185, 28, 28),
        "CODE_BLACK" => Color.FromArgb(248, 17, 24, 39),
        "CODE_ORANGE" => Color.FromArgb(240, 194, 65, 12),
        "CODE_YELLOW" => Color.FromArgb(240, 161, 98, 7),
        "CODE_BLUE" => Color.FromArgb(240, 29, 78, 216),
        "CODE_WHITE" => Color.FromArgb(240, 71, 85, 105),
        "CODE_PINK" => Color.FromArgb(240, 190, 24, 93),
        _ => Color.FromArgb(240, 30, 41, 59)
    };

    private string GetAlertText()
    {
        string code = notice.Code.ToUpperInvariant();
        return code switch
        {
            "CODE_RED" => Loc.T("CodeRedAlert"),
            "CODE_BLACK" => Loc.T("CodeBlackAlert"),
            "CODE_ORANGE" => Loc.T("CodeOrangeAlert"),
            "CODE_YELLOW" => Loc.T("CodeYellowAlert"),
            "CODE_BLUE" => Loc.T("CodeBlueCall", notice.Cabinet ?? "—"),
            "CODE_WHITE" => Loc.T("CodeWhiteCall", notice.Cabinet ?? "—"),
            "CODE_PINK" => Loc.T("CodePinkAlert"),
            _ => !string.IsNullOrEmpty(notice.Title) ? notice.Title : $"ВНИМАНИЕ! {code}!"
        };
    }

    private static string[] GetActionAlgorithm(string code) => code.ToUpperInvariant() switch
    {
        "CODE_RED" => new[]
        {
            "1. Немедленно сообщить в пожарную охрану (101 / 112) и на пост охраны.",
            "2. Задействовать ближайший ручной пожарный извещатель.",
            "3. Отключить вентиляцию и электроприборы, плотно закрыть окна и двери.",
            "4. Организовать эвакуацию по эвакуационным выходам (лифтами пользоваться запрещено!)."
        },
        "CODE_BLACK" => new[]
        {
            "1. Немедленно оповестить службу безопасности и полицию (102 / 112).",
            "2. Категорически запрещено прикасаться, открывать или перемещать подозрительный предмет.",
            "3. Зафиксировать точное время и место обнаружения предмета.",
            "4. Обеспечить оцепление зоны и организованную эвакуацию без паники."
        },
        "CODE_ORANGE" => new[]
        {
            "1. Немедленно изолировать очаг разлива / утечки опасных веществ.",
            "2. Надеть средства индивидуальной защиты (респираторы, перчатки).",
            "3. Оповестить инженерную службу учреждения и МЧС (112).",
            "4. Эвакуировать людей перпендикулярно направлению распространения паров."
        },
        "CODE_YELLOW" => new[]
        {
            "1. Перевести персонал и дежурные службы в режим повышенной готовности.",
            "2. Проверить работоспособность резервных источников питания и связи.",
            "3. Подготовить медицинские комплекты и документацию к возможной транспортировке.",
            "4. Следовать указаниям оперативного штаба ГО и ЧС учреждения."
        },
        "CODE_BLUE" => new[]
        {
            "1. Немедленно вызвать дежурную реанимационную бригаду к месту происшествия.",
            "2. Начать сердечно-лёгочную реанимацию (непрямой массаж сердца, 30:2).",
            "3. Обеспечить свободный доступ к пациенту и доставить дефибриллятор (АНД).",
            "4. Подготовить венозный доступ и кислородную поддержку."
        },
        "CODE_WHITE" => new[]
        {
            "1. Активировать тревожную кнопку вызова группы быстрого реагирования (Охрана).",
            "2. Не вступать в открытое противостояние, сохранять безопасную дистанцию.",
            "3. Оградить пациентов и посетителей от зоны агрессивных действий.",
            "4. Зафиксировать приметы нарушителя и направление его перемещения."
        },
        "CODE_PINK" => new[]
        {
            "1. Немедленно заблокировать все выходы, лестничные клетки и лифты здания.",
            "2. Организовать тотальный досмотр выходящих лиц с детьми, сумками и колясками.",
            "3. Оповестить охрану и полицию (102), передать ориентировку и приметы ребёнка.",
            "4. Провести оперативный осмотр всех палат, кабинетов, туалетов и служебных помещений."
        },
        _ => new[]
        {
            "1. Оповестить дежурного администратора и службу безопасности.",
            "2. Следовать внутреннему регламенту действий учреждения при ЧС.",
            "3. Обеспечить безопасность персонала и пациентов."
        }
    };

    private void BuildUi()
    {
        var themeColor = GetThemeColor();
        var backgroundBrush = new SolidColorBrush(themeColor);

        var rootGrid = new Grid();
        var backdrop = new Border
        {
            Background = backgroundBrush,
            Opacity = 0
        };
        rootGrid.Children.Add(backdrop);

        // Backdrop pulse & fade-in animation
        var fadeIn = new DoubleAnimation(0, 0.96, TimeSpan.FromMilliseconds(400));
        backdrop.BeginAnimation(OpacityProperty, fadeIn);

        var centerCard = new Border
        {
            Width = 840,
            MaxWidth = 960,
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), // Slate 900
            BorderBrush = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(28),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 45,
                ShadowDepth = 0,
                Color = Colors.Black,
                Opacity = 0.8
            },
            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        var scaleTransform = new ScaleTransform(0.6, 0.6);
        centerCard.RenderTransform = scaleTransform;

        // Card entrance zoom animation
        var cardZoom = new DoubleAnimation(0.6, 1.0, TimeSpan.FromMilliseconds(500))
        {
            EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }
        };
        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, cardZoom);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, cardZoom);

        var cardStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        // Test Mode Banner if applicable
        if (isTest)
        {
            var testBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 234, 88, 12)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(240, 251, 146, 60)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 6, 16, 6),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 14)
            };
            testBadge.Child = new TextBlock
            {
                Text = "🧪 ТЕСТОВЫЙ РЕЖИМ (ПРОВЕРКА ТОЛЬКО НА ЭТОМ ПК)",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            cardStack.Children.Add(testBadge);
        }

        // 1. Robot Character (Large & Animated holding tablet)
        robotImage.Width = 168;
        robotImage.Height = 168;
        robotImage.HorizontalAlignment = HorizontalAlignment.Center;
        robotImage.Margin = new Thickness(0, 0, 0, 10);
        RenderOptions.SetBitmapScalingMode(robotImage, BitmapScalingMode.NearestNeighbor);
        UpdateRobotFrame();
        cardStack.Children.Add(robotImage);

        // 2. Alert Badge & Title
        string icon = notice.Code.ToUpperInvariant() switch
        {
            "CODE_RED" => "🚨 🔴",
            "CODE_BLACK" => "⚠️ ⚫",
            "CODE_ORANGE" => "⚠️ 🟠",
            "CODE_YELLOW" => "⚠️ 🟡",
            "CODE_BLUE" => "🏥 🔵",
            "CODE_WHITE" => "🛡️ ⚪",
            "CODE_PINK" => "👶 🌸",
            _ => "🚨"
        };

        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18, 6, 18, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        badge.Child = new TextBlock
        {
            Text = $"{icon} {notice.Code.Replace("CODE_", "КОД ")}",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        cardStack.Children.Add(badge);

        // 3. Main Warning Message
        var messageBlock = new TextBlock
        {
            Text = GetAlertText(),
            FontSize = 24,
            FontWeight = FontWeights.ExtraBold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 14)
        };
        cardStack.Children.Add(messageBlock);

        // 4. Room / Cabinet Info if provided
        if (!string.IsNullOrWhiteSpace(notice.Cabinet))
        {
            var cabinetBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 8, 14, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            };
            cabinetBorder.Child = new TextBlock
            {
                Text = $"Кабинет / Пост: {notice.Cabinet}",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240))
            };
            cardStack.Children.Add(cabinetBorder);
        }

        // 5. Notes / Description if provided
        if (!string.IsNullOrWhiteSpace(notice.Notes))
        {
            var notesBlock = new TextBlock
            {
                Text = notice.Notes,
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 14)
            };
            cardStack.Children.Add(notesBlock);
        }

        // 6. Child Photo for CODE_PINK (with click-to-zoom)
        if (notice.Code.Equals("CODE_PINK", StringComparison.OrdinalIgnoreCase) &&
            (!string.IsNullOrEmpty(notice.ImageBase64) || !string.IsNullOrEmpty(notice.ImageUrl)))
        {
            var photoCard = BuildChildPhotoSection();
            if (photoCard != null) cardStack.Children.Add(photoCard);
        }

        // 7. Step-by-Step Action Algorithm (Held in tablet)
        var algorithmBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(160, 15, 23, 42)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 2, 0, 14),
            MaxWidth = 780
        };
        var algStack = new StackPanel();
        algStack.Children.Add(new TextBlock
        {
            Text = "📋 АЛГОРИТМ ДЕЙСТВИЙ ПРИ ЧС:",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36)), // Amber 400
            Margin = new Thickness(0, 0, 0, 8)
        });
        foreach (var step in GetActionAlgorithm(notice.Code))
        {
            algStack.Children.Add(new TextBlock
            {
                Text = step,
                FontSize = 13.5,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 3)
            });
        }
        algorithmBorder.Child = algStack;
        cardStack.Children.Add(algorithmBorder);

        // 8. Interactive Response Buttons (For CODE_BLUE / CODE_WHITE / Specialist Call)
        bool isSpecialistCall = notice.Code.Equals("CODE_BLUE", StringComparison.OrdinalIgnoreCase) ||
                               notice.Code.Equals("CODE_WHITE", StringComparison.OrdinalIgnoreCase) ||
                               notice.CallId.HasValue;

        if (isSpecialistCall)
        {
            var buttonsRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 10)
            };

            var acceptBtn = new Button
            {
                Content = $"✔ {Loc.T("Confirm")}",
                Width = 200,
                Height = 48,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(16, 185, 129)), // Emerald 500
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 16, 0)
            };
            acceptBtn.Click += (_, _) =>
            {
                onResponse?.Invoke(true);
                CloseWithStatus(Loc.T("CallAcknowledged"));
            };

            var declineBtn = new Button
            {
                Content = $"✖ {Loc.T("Decline")}",
                Width = 200,
                Height = 48,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(71, 85, 105)), // Slate 600
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            declineBtn.Click += (_, _) =>
            {
                onResponse?.Invoke(false);
                CloseWithStatus(Loc.T("CallDeclined"));
            };

            buttonsRow.Children.Add(acceptBtn);
            buttonsRow.Children.Add(declineBtn);
            cardStack.Children.Add(buttonsRow);
        }

        // 9. Countdown Timer & Status
        timerBlock = new TextBlock
        {
            Text = FormatTimer(),
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };
        cardStack.Children.Add(timerBlock);

        // 10. Admin / Test Management Buttons
        var manageRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0)
        };
        if (isTest)
        {
            var endTestBtn = new Button
            {
                Content = "✕ Завершить тестирование",
                Padding = new Thickness(18, 8, 18, 8),
                Background = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(6, 0, 6, 0)
            };
            endTestBtn.Click += (_, _) =>
            {
                onCancel?.Invoke();
                Close();
            };
            manageRow.Children.Add(endTestBtn);
        }
        else if (onCancel != null)
        {
            var cancelBtn = new Button
            {
                Content = "🛑 Сбросить / Отбой тревоги",
                Padding = new Thickness(18, 8, 18, 8),
                Background = new SolidColorBrush(Color.FromRgb(225, 29, 72)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(6, 0, 6, 0)
            };
            cancelBtn.Click += (_, _) =>
            {
                onCancel.Invoke();
                Close();
            };
            manageRow.Children.Add(cancelBtn);
        }
        if (manageRow.Children.Count > 0)
        {
            cardStack.Children.Add(manageRow);
        }

        centerCard.Child = cardStack;
        rootGrid.Children.Add(centerCard);
        Content = rootGrid;
    }

    private FrameworkElement? BuildChildPhotoSection()
    {
        try
        {
            BitmapSource? bmp = null;
            if (!string.IsNullOrEmpty(notice.ImageBase64))
            {
                string raw = notice.ImageBase64;
                if (raw.Contains(",")) raw = raw[(raw.IndexOf(',') + 1)..];
                byte[] bytes = Convert.FromBase64String(raw);
                using var stream = new MemoryStream(bytes);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = stream;
                bi.EndInit();
                bi.Freeze();
                bmp = bi;
            }
            else if (!string.IsNullOrEmpty(notice.ImageUrl) && Uri.TryCreate(notice.ImageUrl, UriKind.Absolute, out var uri))
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = uri;
                bi.EndInit();
                bi.Freeze();
                bmp = bi;
            }

            if (bmp == null) return null;

            var container = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            };

            var photoBorder = new Border
            {
                Width = 240,
                Height = 240,
                CornerRadius = new CornerRadius(16),
                BorderBrush = new SolidColorBrush(Color.FromRgb(244, 114, 182)), // Pink 400
                BorderThickness = new Thickness(3),
                ClipToBounds = true,
                Cursor = Cursors.Hand,
                ToolTip = Loc.T("ViewPhotoFull")
            };

            var photoImg = new Image
            {
                Source = bmp,
                Stretch = Stretch.UniformToFill
            };
            photoBorder.Child = photoImg;

            photoBorder.MouseLeftButtonUp += (_, _) => OpenPhotoLightbox(bmp);

            var hint = new TextBlock
            {
                Text = Loc.T("ViewPhotoFull"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(244, 114, 182)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0)
            };

            container.Children.Add(photoBorder);
            container.Children.Add(hint);
            return container;
        }
        catch { return null; }
    }

    private void OpenPhotoLightbox(BitmapSource bmp)
    {
        var lightbox = new Window
        {
            WindowStyle = WindowStyle.None,
            WindowState = WindowState.Maximized,
            Background = new SolidColorBrush(Color.FromArgb(240, 10, 10, 15)),
            Topmost = true
        };

        var grid = new Grid();
        var img = new Image
        {
            Source = bmp,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(40)
        };
        grid.Children.Add(img);

        var closeBtn = new Button
        {
            Content = "✖ Закрыть просмотр",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 24, 24, 0),
            Padding = new Thickness(16, 8, 16, 8),
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        closeBtn.Click += (_, _) => lightbox.Close();
        grid.Children.Add(closeBtn);

        lightbox.Content = grid;
        lightbox.ShowDialog();
    }

    private void CloseWithStatus(string msg)
    {
        timer.Stop();
        animTimer.Stop();
        Close();
    }

    private string FormatTimer() => $"Активно: {remainingSeconds / 60:D2}:{remainingSeconds % 60:D2}";

    private void StartTimers()
    {
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) =>
        {
            remainingSeconds--;
            if (timerBlock != null) timerBlock.Text = FormatTimer();
            if (remainingSeconds <= 0)
            {
                timer.Stop();
                animTimer.Stop();
                Close();
            }
        };
        timer.Start();

        petState = notice.Code.ToUpperInvariant() switch
        {
            "CODE_RED" or "CODE_BLACK" => PetState.Surprise,
            "CODE_ORANGE" or "CODE_YELLOW" => PetState.Notice,
            "CODE_BLUE" or "CODE_WHITE" => PetState.Action,
            "CODE_PINK" => PetState.Sad,
            _ => PetState.Notice
        };

        animTimer.Interval = TimeSpan.FromMilliseconds(120);
        animTimer.Tick += (_, _) =>
        {
            spriteFrame++;
            UpdateRobotFrame();
        };
        animTimer.Start();
    }

    private void UpdateRobotFrame()
    {
        var frame = sprites.Get(petState, spriteFrame);
        robotImage.Source = frame.Image;
    }
}
