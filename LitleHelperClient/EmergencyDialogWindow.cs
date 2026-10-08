using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PixelHelper;

public sealed class EmergencyDialogWindow : Window
{
    private readonly HubConnectionService hub;
    private readonly Settings settings;

    private readonly Grid mainContainer = new();
    private string currentCabinet = "";
    private string? pinkPhotoBase64 = null;
    private string? pinkPhotoFileName = null;
    private List<string> departments = new();

    public EmergencyDialogWindow(HubConnectionService hub, Settings settings)
    {
        this.hub = hub;
        this.settings = settings;

        currentCabinet = settings.TicketRoom?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(currentCabinet))
            currentCabinet = Environment.MachineName;

        Title = Loc.T("ActionEmergency");
        Width = 580;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = true;
        Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)); // Slate 900
        Foreground = Brushes.White;
        WindowStyle = WindowStyle.None;

        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            Topmost = true;
            _ = LoadDepartmentsAsync();
        };

        Content = BuildRootFrame();
        ShowMainCodesView();
    }

    private async Task LoadDepartmentsAsync()
    {
        try
        {
            departments = await hub.GetSpecialistDepartmentsAsync();
        }
        catch
        {
            departments = new() { "Дежурный врач", "Реаниматолог", "Хирург", "Травматолог", "Охрана / Служба безопасности" };
        }
    }

    private FrameworkElement BuildRootFrame()
    {
        var rootBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)), // Slate 700
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42))
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header bar
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Dynamic Content

        // Custom Title Bar
        var titleBar = new Grid
        {
            Margin = new Thickness(16, 12, 16, 8),
            Background = Brushes.Transparent
        };
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal };
        titleStack.Children.Add(new TextBlock
        {
            Text = "🚨 ЭКСТРЕННОЕ ОПОВЕЩЕНИЕ",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113)) // Red 400
        });

        var closeBtn = new Button
        {
            Content = "✕",
            Width = 32,
            Height = 32,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = Cursors.Hand
        };
        closeBtn.Click += (_, _) => Close();

        titleBar.Children.Add(titleStack);
        titleBar.Children.Add(closeBtn);
        Grid.SetRow(titleBar, 0);
        layout.Children.Add(titleBar);

        mainContainer.Margin = new Thickness(16, 0, 16, 16);
        Grid.SetRow(mainContainer, 1);
        layout.Children.Add(mainContainer);

        rootBorder.Child = layout;
        return rootBorder;
    }

    private ControlTemplate CreateRoundedButtonTemplate(int cornerRadius = 8)
    {
        var template = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));
        borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Button.Background)) { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        borderFactory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding(nameof(Button.BorderBrush)) { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        borderFactory.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding(nameof(Button.BorderThickness)) { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });

        var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        borderFactory.AppendChild(contentFactory);

        template.VisualTree = borderFactory;
        return template;
    }

    private Button CreateStyledButton(string title, string subtitle, Color bg, Action onClick, int height = 54)
    {
        var btn = new Button
        {
            Height = height,
            Background = new SolidColorBrush(bg),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Foreground = Brushes.White,
            Cursor = Cursors.Hand,
            Margin = new Thickness(4),
            Template = CreateRoundedButtonTemplate(10)
        };

        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.ExtraBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brushes.White
        });

        if (!string.IsNullOrEmpty(subtitle))
        {
            sp.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 9.5,
                Opacity = 0.85,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        btn.Content = sp;
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private void ShowMainCodesView()
    {
        mainContainer.Children.Clear();
        mainContainer.RowDefinitions.Clear();
        mainContainer.ColumnDefinitions.Clear();

        var rootStack = new StackPanel();

        // Cabinet Input Row
        var cabGrid = new Grid { Margin = new Thickness(4, 0, 4, 10) };
        cabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var cabLabel = new TextBlock
        {
            Text = "Кабинет / Помещение:",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(cabLabel, 0);

        var cabInput = new TextBox
        {
            Text = currentCabinet,
            Height = 32,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4, 8, 4),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        cabInput.TextChanged += (_, _) => currentCabinet = cabInput.Text.Trim();
        Grid.SetColumn(cabInput, 1);

        cabGrid.Children.Add(cabLabel);
        cabGrid.Children.Add(cabInput);
        rootStack.Children.Add(cabGrid);

        // Codes Grid: 3 columns
        var codesGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        codesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        codesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        codesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        codesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1
        codesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 2
        codesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 3 (Pink)

        // Row 1: -КОД КРАСНЫЙ- -КОД ЧЁРНЫЙ- -КОД ОРАНЖЕВЫЙ-
        var btnRed = CreateStyledButton("-КОД КРАСНЫЙ-", "🔴 Пожар / Эвакуация", Color.FromRgb(220, 38, 38), () => ShowConfirm("CODE_RED", "КОД КРАСНЫЙ: Пожар / Задымление", Color.FromRgb(220, 38, 38)));
        Grid.SetRow(btnRed, 0); Grid.SetColumn(btnRed, 0);
        codesGrid.Children.Add(btnRed);

        var btnBlack = CreateStyledButton("-КОД ЧЁРНЫЙ-", "⚫ Теракт / Угроза", Color.FromRgb(24, 24, 27), () => ShowConfirm("CODE_BLACK", "КОД ЧЁРНЫЙ: Угроза взрыва / Теракт", Color.FromRgb(24, 24, 27)));
        Grid.SetRow(btnBlack, 0); Grid.SetColumn(btnBlack, 1);
        codesGrid.Children.Add(btnBlack);

        var btnOrange = CreateStyledButton("-КОД ОРАНЖЕВЫЙ-", "🟠 Опасные вещества", Color.FromRgb(234, 88, 12), () => ShowConfirm("CODE_ORANGE", "КОД ОРАНЖЕВЫЙ: Опасные вещества / ЧС", Color.FromRgb(234, 88, 12)));
        Grid.SetRow(btnOrange, 0); Grid.SetColumn(btnOrange, 2);
        codesGrid.Children.Add(btnOrange);

        // Row 2: -КОД ЖЁЛТЫЙ- -КОД СИНИЙ- -КОД БЕЛЫЙ-
        var btnYellow = CreateStyledButton("-КОД ЖЁЛТЫЙ-", "🟡 Чрезвычайная ситуация", Color.FromRgb(217, 119, 6), () => ShowConfirm("CODE_YELLOW", "КОД ЖЁЛТЫЙ: Чрезвычайная ситуация", Color.FromRgb(217, 119, 6)));
        Grid.SetRow(btnYellow, 1); Grid.SetColumn(btnYellow, 0);
        codesGrid.Children.Add(btnYellow);

        var btnBlue = CreateStyledButton("-КОД СИНИЙ-", "🔵 Остановка сердца / СЛР", Color.FromRgb(37, 99, 235), () => ShowConfirm("CODE_BLUE", "КОД СИНИЙ: Реанимация / Остановка сердца", Color.FromRgb(37, 99, 235)));
        Grid.SetRow(btnBlue, 1); Grid.SetColumn(btnBlue, 1);
        codesGrid.Children.Add(btnBlue);

        var btnWhite = CreateStyledButton("-КОД БЕЛЫЙ-", "⚪ Агрессия / Охрана", Color.FromRgb(71, 85, 105), () => ShowConfirm("CODE_WHITE", "КОД БЕЛЫЙ: Агрессия / Нападение", Color.FromRgb(71, 85, 105)));
        Grid.SetRow(btnWhite, 1); Grid.SetColumn(btnWhite, 2);
        codesGrid.Children.Add(btnWhite);

        // Row 3: -КОД РОЗОВЫЙ- (Span all 3 columns)
        string pinkSub = string.IsNullOrEmpty(pinkPhotoBase64) ? "🌸 Потеря / Похищение ребёнка (возможно с фото)" : $"🌸 Прикреплено фото: {pinkPhotoFileName}";
        var btnPink = CreateStyledButton("-КОД РОЗОВЫЙ-", pinkSub, Color.FromRgb(219, 39, 119), () => ShowPinkCodeView(), 52);
        Grid.SetRow(btnPink, 2); Grid.SetColumn(btnPink, 0); Grid.SetColumnSpan(btnPink, 3);
        codesGrid.Children.Add(btnPink);

        rootStack.Children.Add(codesGrid);

        // Row 4: ВЫЗОВ СПЕЦИАЛИСТА (Wide button)
        var btnSpecialist = CreateStyledButton("🩺  -ВЫЗОВ СПЕЦИАЛИСТА-", "Срочный вызов дежурного врача / реаниматолога / хирурга", Color.FromRgb(5, 150, 105), () => ShowSpecialistView(), 52);
        btnSpecialist.Margin = new Thickness(4, 4, 4, 4);
        rootStack.Children.Add(btnSpecialist);

        // Row 5: ОТМЕНА (Wide button)
        var btnCancel = CreateStyledButton("✕  ОТМЕНА", "", Color.FromRgb(51, 65, 85), () => Close(), 42);
        btnCancel.Margin = new Thickness(4, 4, 4, 4);
        rootStack.Children.Add(btnCancel);

        mainContainer.Children.Add(rootStack);
    }

    private void ShowConfirm(string code, string title, Color color)
    {
        mainContainer.Children.Clear();
        var panel = new StackPanel { Margin = new Thickness(8) };

        var badge = new Border
        {
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 16)
        };
        badge.Child = new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center
        };
        panel.Children.Add(badge);

        panel.Children.Add(new TextBlock
        {
            Text = "Кабинет / Помещение вызова:",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            Margin = new Thickness(0, 0, 0, 6)
        });

        var cabBox = new TextBox
        {
            Text = currentCabinet,
            Height = 34,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 14)
        };
        cabBox.TextChanged += (_, _) => currentCabinet = cabBox.Text.Trim();
        panel.Children.Add(cabBox);

        panel.Children.Add(new TextBlock
        {
            Text = "Дополнительные примечания (необязательно):",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            Margin = new Thickness(0, 0, 0, 6)
        });

        var notesBox = new TextBox
        {
            Height = 50,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 20)
        };
        panel.Children.Add(notesBox);

        Button? triggerBtn = null;
        triggerBtn = CreateStyledButton("🚨 ПОДТВЕРДИТЬ И ЗАПУСТИТЬ ОПОВЕЩЕНИЕ", "Оповещение будет выведено на все экраны учреждения", color, async () =>
        {
            try
            {
                if (triggerBtn != null) triggerBtn.IsEnabled = false;
                await hub.TriggerEmergencyAlertAsync(code, cabBox.Text.Trim(), notesBox.Text.Trim(), null, null);
                MessageBox.Show(this, "Сигнал экстренного оповещения успешно отправлен!", "Оповещение", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Ошибка передачи оповещения: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                if (triggerBtn != null) triggerBtn.IsEnabled = true;
            }
        }, 54);
        panel.Children.Add(triggerBtn);

        var backBtn = CreateStyledButton("‹  НАЗАД К ВЫБОРУ КОДОВ", "", Color.FromRgb(51, 65, 85), ShowMainCodesView, 40);
        backBtn.Margin = new Thickness(4, 8, 4, 0);
        panel.Children.Add(backBtn);

        mainContainer.Children.Add(panel);
    }

    private void ShowPinkCodeView()
    {
        mainContainer.Children.Clear();
        var panel = new StackPanel { Margin = new Thickness(8) };

        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(219, 39, 119)), // Pink 600
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 0, 0, 12)
        };
        badge.Child = new TextBlock
        {
            Text = "🌸 КОД РОЗОВЫЙ: Потеря / Похищение ребёнка",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center
        };
        panel.Children.Add(badge);

        panel.Children.Add(new TextBlock
        {
            Text = "Кабинет / Место происшествия:",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            Margin = new Thickness(0, 0, 0, 4)
        });

        var cabBox = new TextBox
        {
            Text = currentCabinet,
            Height = 32,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 10)
        };
        cabBox.TextChanged += (_, _) => currentCabinet = cabBox.Text.Trim();
        panel.Children.Add(cabBox);

        panel.Children.Add(new TextBlock
        {
            Text = "Приметы / описание ребёнка (возраст, одежда):",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            Margin = new Thickness(0, 0, 0, 4)
        });

        var notesBox = new TextBox
        {
            Height = 44,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(notesBox);

        // Photo Upload Section
        var photoBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 16)
        };

        var photoStack = new StackPanel();
        if (string.IsNullOrEmpty(pinkPhotoBase64))
        {
            var uploadBtn = CreateStyledButton("📷  Загрузить фото ребёнка (необязательно)", "Фотография отобразится на всех экранах ПК", Color.FromRgb(79, 70, 229), () =>
            {
                var ofd = new OpenFileDialog
                {
                    Title = "Выберите фотографию ребёнка",
                    Filter = "Изображения (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|Все файлы (*.*)|*.*"
                };
                if (ofd.ShowDialog() == true)
                {
                    pinkPhotoBase64 = ImageFileToBase64(ofd.FileName);
                    pinkPhotoFileName = Path.GetFileName(ofd.FileName);
                    ShowPinkCodeView();
                }
            }, 44);
            photoStack.Children.Add(uploadBtn);
        }
        else
        {
            var previewGrid = new Grid();
            previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            try
            {
                var bytes = Convert.FromBase64String(pinkPhotoBase64);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = new MemoryStream(bytes);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();

                var img = new Image
                {
                    Source = bi,
                    Width = 50,
                    Height = 50,
                    Stretch = Stretch.UniformToFill
                };
                var imgBorder = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    ClipToBounds = true,
                    Child = img,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                Grid.SetColumn(imgBorder, 0);
                previewGrid.Children.Add(imgBorder);
            }
            catch { }

            var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            infoStack.Children.Add(new TextBlock
            {
                Text = "✓ Фото прикреплено",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153))
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = pinkPhotoFileName ?? "image.jpg",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184))
            });
            Grid.SetColumn(infoStack, 1);
            previewGrid.Children.Add(infoStack);

            var clearBtn = new Button
            {
                Content = "✕ Удалить",
                Height = 32,
                Padding = new Thickness(10, 4, 10, 4),
                Background = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                Foreground = Brushes.White,
                Cursor = Cursors.Hand,
                Template = CreateRoundedButtonTemplate(6)
            };
            clearBtn.Click += (_, _) =>
            {
                pinkPhotoBase64 = null;
                pinkPhotoFileName = null;
                ShowPinkCodeView();
            };
            Grid.SetColumn(clearBtn, 2);
            previewGrid.Children.Add(clearBtn);

            photoStack.Children.Add(previewGrid);
        }

        photoBorder.Child = photoStack;
        panel.Children.Add(photoBorder);

        Button? triggerBtn = null;
        triggerBtn = CreateStyledButton("🚨 ПЕРЕДАТЬ ТРЕВОГУ «КОД РОЗОВЫЙ»", "Оповещение с фото будет передано на все ПК", Color.FromRgb(219, 39, 119), async () =>
        {
            try
            {
                if (triggerBtn != null) triggerBtn.IsEnabled = false;
                await hub.TriggerEmergencyAlertAsync("CODE_PINK", cabBox.Text.Trim(), notesBox.Text.Trim(), pinkPhotoBase64, null);
                MessageBox.Show(this, "Сигнал «КОД РОЗОВЫЙ» успешно передан на все ПК!", "Оповещение", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Ошибка передачи оповещения: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                if (triggerBtn != null) triggerBtn.IsEnabled = true;
            }
        }, 50);
        panel.Children.Add(triggerBtn);

        var backBtn = CreateStyledButton("‹  НАЗАД К ВЫБОРУ КОДОВ", "", Color.FromRgb(51, 65, 85), ShowMainCodesView, 38);
        backBtn.Margin = new Thickness(4, 6, 4, 0);
        panel.Children.Add(backBtn);

        mainContainer.Children.Add(panel);
    }

    private void ShowSpecialistView()
    {
        mainContainer.Children.Clear();
        var panel = new StackPanel { Margin = new Thickness(8) };

        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(5, 150, 105)), // Emerald 600
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 0, 0, 12)
        };
        badge.Child = new TextBlock
        {
            Text = "🩺 СРОЧНЫЙ ВЫЗОВ СПЕЦИАЛИСТА В КАБИНЕТ",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center
        };
        panel.Children.Add(badge);

        panel.Children.Add(new TextBlock
        {
            Text = "Кабинет / Помещение вызова:",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            Margin = new Thickness(0, 0, 0, 4)
        });

        var cabBox = new TextBox
        {
            Text = currentCabinet,
            Height = 32,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 10)
        };
        cabBox.TextChanged += (_, _) => currentCabinet = cabBox.Text.Trim();
        panel.Children.Add(cabBox);

        panel.Children.Add(new TextBlock
        {
            Text = "Выберите профиль специалиста:",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            Margin = new Thickness(0, 0, 0, 4)
        });

        var deptCombo = new ComboBox
        {
            Height = 34,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var deptList = departments.Count > 0 ? departments : new() { "Дежурный врач", "Реаниматолог", "Хирург", "Травматолог", "Охрана / Служба безопасности" };
        foreach (var d in deptList)
            deptCombo.Items.Add(d);
        deptCombo.SelectedIndex = 0;
        panel.Children.Add(deptCombo);

        panel.Children.Add(new TextBlock
        {
            Text = "Причина вызова (необязательно):",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            Margin = new Thickness(0, 0, 0, 4)
        });

        var notesBox = new TextBox
        {
            Height = 44,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 16)
        };
        panel.Children.Add(notesBox);

        Button? triggerBtn = null;
        triggerBtn = CreateStyledButton("🚨 ВЫЗВАТЬ СПЕЦИАЛИСТА В КАБИНЕТ", "Специалисту будет направлен вызов на рабочий экран", Color.FromRgb(5, 150, 105), async () =>
        {
            try
            {
                if (triggerBtn != null) triggerBtn.IsEnabled = false;
                string selectedDept = deptCombo.SelectedItem?.ToString() ?? "Дежурный специалист";
                await hub.TriggerEmergencyAlertAsync("SPECIALIST_CALL", cabBox.Text.Trim(), notesBox.Text.Trim(), null, selectedDept);
                MessageBox.Show(this, $"Вызов отправлен специалисту ({selectedDept})!", "Оповещение", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Ошибка вызова специалиста: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                if (triggerBtn != null) triggerBtn.IsEnabled = true;
            }
        }, 50);
        panel.Children.Add(triggerBtn);

        var backBtn = CreateStyledButton("‹  НАЗАД К ВЫБОРУ КОДОВ", "", Color.FromRgb(51, 65, 85), ShowMainCodesView, 38);
        backBtn.Margin = new Thickness(4, 6, 4, 0);
        panel.Children.Add(backBtn);

        mainContainer.Children.Add(panel);
    }

    private static string? ImageFileToBase64(string filePath)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            double maxDim = Math.Max(bitmap.PixelWidth, bitmap.PixelHeight);
            BitmapSource source = bitmap;
            if (maxDim > 1200)
            {
                double scale = 1200.0 / maxDim;
                source = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
            }

            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
            return null;
        }
    }
}

