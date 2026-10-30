using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PixelHelper;

public sealed class UserTicketsWindow : Window
{
    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(nint hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, nint hdcSrc, int nXSrc, int nYSrc, int dwRop);
    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint hdc, int nWidth, int nHeight);
    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint hgdiobj);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint hObject);
    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")]
    private static extern nint GetWindowDC(nint hWnd);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    private readonly Settings settings;
    private readonly PetWindow? petWindow;
    private readonly HubConnectionService hub;
    private readonly CancellationTokenSource lifetime = new();

    private readonly Grid rootGrid = new();
    private TextBlock statusText = null!;

    // Navigation buttons
    private Button btnTabCreate = null!;
    private Button btnTabMy = null!;
    private ContentControl moduleHost = null!;
    private int activeTab = 0; // 0 = Create, 1 = My Tickets

    // Create Ticket form state
    private TextBox txtTitle = null!;
    private TextBox txtDesc = null!;
    private ComboBox cmbBranch = null!;
    private TextBox txtRoom = null!;
    private StackPanel attachmentsPanel = null!;
    private Button btnSubmitTicket = null!;
    private readonly List<TicketAttachmentDto> newAttachments = [];
    private List<TicketBranch> branches = [];

    // My Tickets state
    private List<UserTicketSummaryDto> myTickets = [];

    public UserTicketsWindow(Settings settings, HubConnectionService hub, PetWindow? petWindow = null)
    {
        this.settings = settings;
        this.hub = hub;
        this.petWindow = petWindow;

        Title = "Заявки в техподдержку";
        Width = 920;
        Height = 720;
        MinWidth = 760;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = (Brush)new BrushConverter().ConvertFromString("#F8FAFC")!;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif");

        BuildShell();
        Closed += (_, _) =>
        {
            lifetime.Cancel();
            newAttachments.Clear();
            myTickets.Clear();
        };

        KeyDown += OnWindowKeyDown;

        Loaded += async (_, _) =>
        {
            await LoadTicketContextAsync();
            await LoadMyTicketsAsync();
        };
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (activeTab == 0 && e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            try
            {
                if (Clipboard.ContainsImage())
                {
                    var img = Clipboard.GetImage();
                    if (img != null)
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(img));
                        using var ms = new MemoryStream();
                        encoder.Save(ms);
                        byte[] bytes = ms.ToArray();
                        newAttachments.Add(new TicketAttachmentDto($"screenshot_{DateTime.Now:HHmmss}.png", bytes));
                        RenderAttachmentsList();
                        e.Handled = true;
                    }
                }
            }
            catch { }
        }
    }

    #region 1. Shell & Header Layout

    private void BuildShell()
    {
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Top Header
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: Navigation Tabs
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 2: Content

        // 0. Header Bar
        var header = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#0F172A")!,
            Padding = new Thickness(20, 14, 20, 14)
        };
        var hDock = new DockPanel();

        var hLeft = new StackPanel { Orientation = Orientation.Horizontal };
        hLeft.Children.Add(new TextBlock
        {
            Text = "📋",
            FontSize = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        });
        var hTitles = new StackPanel();
        hTitles.Children.Add(new TextBlock
        {
            Text = "Заявки в техподдержку",
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White
        });
        hTitles.Children.Add(new TextBlock
        {
            Text = $"ПК: {Environment.MachineName}  ·  Пользователь: {Environment.UserName}",
            FontSize = 12,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#94A3B8")!
        });
        hLeft.Children.Add(hTitles);
        hDock.Children.Add(hLeft);

        var hRight = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnRefresh = new Button
        {
            Content = "🔄 Обновить",
            FontSize = 12,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(8, 0, 0, 0),
            Background = (Brush)new BrushConverter().ConvertFromString("#334155")!,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        btnRefresh.Click += async (_, _) =>
        {
            await LoadTicketContextAsync();
            await LoadMyTicketsAsync();
        };
        hRight.Children.Add(btnRefresh);
        DockPanel.SetDock(hRight, Dock.Right);
        hDock.Children.Add(hRight);

        header.Child = hDock;
        Grid.SetRow(header, 0);
        rootGrid.Children.Add(header);

        // 1. Navigation Tabs
        var tabsBar = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#E2E8F0")!,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(20, 10, 20, 10)
        };
        var tDock = new DockPanel();

        var tabsStack = new StackPanel { Orientation = Orientation.Horizontal };
        btnTabCreate = MakeTabButton("📝 Создать заявку", 0);
        btnTabMy = MakeTabButton("📂 Мои заявки", 1);
        tabsStack.Children.Add(btnTabCreate);
        tabsStack.Children.Add(btnTabMy);
        tDock.Children.Add(tabsStack);

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

        // 2. Module Host
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

        StyleBtn(btnTabCreate, activeTab == 0);
        StyleBtn(btnTabMy, activeTab == 1);
    }

    private void RenderActiveModule()
    {
        if (activeTab == 0)
            RenderCreateTicketModule();
        else
            RenderMyTicketsModule();
    }

    #endregion

    #region 2. Module 0: Create Ticket Form

    private void RenderCreateTicketModule()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(24),
            Margin = new Thickness(20),
            MaxWidth = 820,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        card.AllowDrop = true;
        card.Drop += OnCardDrop;

        var form = new StackPanel();

        form.Children.Add(new TextBlock
        {
            Text = "Новое обращение в службу ИТ",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#0F172A")!,
            Margin = new Thickness(0, 0, 0, 4)
        });
        form.Children.Add(new TextBlock
        {
            Text = "Опишите вашу проблему, укажите кабинет и при необходимости прикрепите снимок экрана или файл.",
            FontSize = 13,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#64748B")!,
            Margin = new Thickness(0, 0, 0, 18)
        });

        // 1. Title
        form.Children.Add(new TextBlock { Text = "Тема обращения *", FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 0, 0, 6) });
        txtTitle = new TextBox
        {
            Height = 36,
            FontSize = 13.5,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 0, 14),
            ToolTip = "Кратко: например, Не печатает принтер или Не открывается 1С"
        };
        form.Children.Add(txtTitle);

        // 2. Branch & Room row
        var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });

        var bStack = new StackPanel();
        bStack.Children.Add(new TextBlock { Text = "Филиал / Подразделение *", FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 0, 0, 6) });
        cmbBranch = new ComboBox { Height = 36, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
        bStack.Children.Add(cmbBranch);
        Grid.SetColumn(bStack, 0);
        rowGrid.Children.Add(bStack);

        var rStack = new StackPanel();
        rStack.Children.Add(new TextBlock { Text = "Кабинет *", FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 0, 0, 6) });
        txtRoom = new TextBox { Height = 36, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 0, 8, 0), Text = settings.TicketRoom ?? "" };
        rStack.Children.Add(txtRoom);
        Grid.SetColumn(rStack, 2);
        rowGrid.Children.Add(rStack);

        form.Children.Add(rowGrid);
        PopulateBranchesDropdown();

        // 3. Description
        form.Children.Add(new TextBlock { Text = "Подробное описание проблемы *", FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 0, 0, 6) });
        txtDesc = new TextBox
        {
            Height = 120,
            FontSize = 13,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 14)
        };
        form.Children.Add(txtDesc);

        // 4. Attachments Bar
        var attDock = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        attDock.Children.Add(new TextBlock
        {
            Text = "Прикрепленные файлы (до 5 файлов)",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });

        var attButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnScreen = new Button
        {
            Content = "📸 Снимок экрана",
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            Background = (Brush)new BrushConverter().ConvertFromString("#F1F5F9")!,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!
        };
        btnScreen.Click += async (_, _) => await CaptureScreenshotAsync();
        attButtons.Children.Add(btnScreen);

        var btnAddFile = new Button
        {
            Content = "📎 Прикрепить файл",
            Padding = new Thickness(10, 5, 10, 5),
            Cursor = Cursors.Hand,
            Background = (Brush)new BrushConverter().ConvertFromString("#F1F5F9")!,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!
        };
        btnAddFile.Click += (_, _) => BrowseAttachment();
        attButtons.Children.Add(btnAddFile);

        DockPanel.SetDock(attButtons, Dock.Right);
        attDock.Children.Add(attButtons);
        form.Children.Add(attDock);

        attachmentsPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        form.Children.Add(attachmentsPanel);
        RenderAttachmentsList();

        // 5. Submit Button
        btnSubmitTicket = new Button
        {
            Content = "🚀 Отправить заявку",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Background = (Brush)new BrushConverter().ConvertFromString("#2563EB")!,
            Foreground = Brushes.White,
            Height = 44,
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(0)
        };
        btnSubmitTicket.Click += async (_, _) => await SubmitNewTicketAsync();
        form.Children.Add(btnSubmitTicket);

        card.Child = form;
        scroll.Content = card;
        moduleHost.Content = scroll;
    }

    private void PopulateBranchesDropdown()
    {
        if (cmbBranch == null) return;
        cmbBranch.Items.Clear();

        int? selectedIndex = null;
        for (int i = 0; i < branches.Count; i++)
        {
            var b = branches[i];
            var item = new ComboBoxItem { Content = b.Name, Tag = b.Id };
            cmbBranch.Items.Add(item);

            if (b.Id == settings.TicketBranchId) selectedIndex = i;
        }

        if (selectedIndex.HasValue)
            cmbBranch.SelectedIndex = selectedIndex.Value;
        else if (cmbBranch.Items.Count > 0)
            cmbBranch.SelectedIndex = 0;
    }

    private void RenderAttachmentsList()
    {
        if (attachmentsPanel == null) return;
        attachmentsPanel.Children.Clear();

        if (newAttachments.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = "Перетащите файлы сюда или нажмите «Прикрепить файл» / «Снимок экрана» (или нажмите Ctrl+V).",
                FontSize = 12,
                Foreground = (Brush)new BrushConverter().ConvertFromString("#94A3B8")!,
                Margin = new Thickness(0, 4, 0, 4)
            };
            attachmentsPanel.Children.Add(emptyText);
            return;
        }

        foreach (var att in newAttachments.ToList())
        {
            var border = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString("#F8FAFC")!,
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#E2E8F0")!,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var dock = new DockPanel();

            var delBtn = new Button
            {
                Content = "✕",
                Foreground = (Brush)new BrushConverter().ConvertFromString("#EF4444")!,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(6, 2, 6, 2)
            };
            delBtn.Click += (_, _) =>
            {
                newAttachments.Remove(att);
                RenderAttachmentsList();
            };
            DockPanel.SetDock(delBtn, Dock.Right);
            dock.Children.Add(delBtn);

            string sizeStr = att.Content != null ? $"{att.Content.Length / 1024} КБ" : "";
            dock.Children.Add(new TextBlock
            {
                Text = $"📄 {att.FileName} ({sizeStr})",
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center
            });

            border.Child = dock;
            attachmentsPanel.Children.Add(border);
        }
    }

    private void BrowseAttachment()
    {
        if (newAttachments.Count >= 5)
        {
            MessageBox.Show("Максимум 5 файлов для одной заявки.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "Выберите файл для прикрепления к заявке",
            Multiselect = true,
            Filter = "Все файлы (*.*)|*.*|Изображения (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|Документы (*.pdf;*.docx;*.xlsx;*.txt)|*.pdf;*.docx;*.xlsx;*.txt"
        };
        if (dlg.ShowDialog() == true)
        {
            foreach (var path in dlg.FileNames)
            {
                AddAttachmentFromFile(path);
            }
            RenderAttachmentsList();
        }
    }

    private void AddAttachmentFromFile(string path)
    {
        if (newAttachments.Count >= 5) return;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return;
            if (info.Length > 15 * 1024 * 1024)
            {
                MessageBox.Show($"Файл {info.Name} превышает 15 МБ.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            byte[] bytes = File.ReadAllBytes(path);
            newAttachments.Add(new TicketAttachmentDto(info.Name, bytes));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось прочитать файл: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCardDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null)
            {
                foreach (var f in files) AddAttachmentFromFile(f);
                RenderAttachmentsList();
            }
        }
    }

    private async Task CaptureScreenshotAsync()
    {
        if (newAttachments.Count >= 5)
        {
            MessageBox.Show("Максимум 5 файлов для одной заявки.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        WindowState = WindowState.Minimized;
        await Task.Delay(350);

        try
        {
            int screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            int screenHeight = (int)SystemParameters.PrimaryScreenHeight;

            nint hDesk = GetDesktopWindow();
            nint hdcSrc = GetWindowDC(hDesk);
            nint hdcDest = CreateCompatibleDC(hdcSrc);
            nint hBitmap = CreateCompatibleBitmap(hdcSrc, screenWidth, screenHeight);
            nint hOld = SelectObject(hdcDest, hBitmap);
            BitBlt(hdcDest, 0, 0, screenWidth, screenHeight, hdcSrc, 0, 0, 0x00CC0020 /* SRCCOPY */);
            SelectObject(hdcDest, hOld);
            DeleteDC(hdcDest);
            ReleaseDC(hDesk, hdcSrc);

            var bs = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, nint.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            DeleteObject(hBitmap);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bs));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            byte[] bytes = ms.ToArray();

            string name = $"screenshot_{DateTime.Now:HHmmss}.png";
            newAttachments.Add(new TicketAttachmentDto(name, bytes));
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось сделать снимок: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            WindowState = WindowState.Normal;
            Activate();
            RenderAttachmentsList();
        }
    }

    private async Task SubmitNewTicketAsync()
    {
        string title = txtTitle.Text.Trim();
        string desc = txtDesc.Text.Trim();
        string room = txtRoom.Text.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            MessageBox.Show("Пожалуйста, укажите тему обращения.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            txtTitle.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(desc))
        {
            MessageBox.Show("Пожалуйста, подробно опишите проблему.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            txtDesc.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(room))
        {
            MessageBox.Show("Пожалуйста, укажите номер вашего кабинета.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            txtRoom.Focus();
            return;
        }

        int? branchId = cmbBranch?.SelectedItem is ComboBoxItem item && item.Tag is int bid ? bid : null;

        btnSubmitTicket.IsEnabled = false;
        btnSubmitTicket.Content = "Отправка заявки…";
        statusText.Text = "Создание заявки в GLPI…";

        try
        {
            int id = await hub.CreateTicketAtWithFilesAsync(title, desc, branchId, room, Environment.UserName,
                newAttachments.Count > 0 ? newAttachments : null, lifetime.Token);

            settings.TicketRoom = room;
            settings.TicketBranchId = branchId;
            try { settings.Save(); } catch { }

            MessageBox.Show($"Заявка №{id} успешно создана и передана специалистам!", "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);

            // Clear form
            txtTitle.Text = "";
            txtDesc.Text = "";
            newAttachments.Clear();
            RenderAttachmentsList();

            // Auto-switch to My Tickets
            activeTab = 1;
            UpdateTabStyles();
            await LoadMyTicketsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка отправки заявки: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            statusText.Text = "Ошибка создания заявки";
        }
        finally
        {
            btnSubmitTicket.IsEnabled = true;
            btnSubmitTicket.Content = "🚀 Отправить заявку";
        }
    }

    #endregion

    #region 3. Module 1: My Tickets View

    private void RenderMyTicketsModule()
    {
        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Filters bar
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Cards list

        // Top Filters
        var filterDock = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };

        var searchBox = new TextBox
        {
            Height = 34,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 0, 10, 0),
            ToolTip = "Поиск по номеру, теме или кабинету…"
        };

        var fRight = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        var cmbPeriod = new ComboBox { Height = 34, Margin = new Thickness(8, 0, 0, 0) };
        cmbPeriod.Items.Add(new ComboBoxItem { Content = "📅 За всё время", Tag = "all" });
        cmbPeriod.Items.Add(new ComboBoxItem { Content = "Сегодня", Tag = "today" });
        cmbPeriod.Items.Add(new ComboBoxItem { Content = "За 7 дней", Tag = "7days" });
        cmbPeriod.Items.Add(new ComboBoxItem { Content = "За 30 дней", Tag = "30days" });
        cmbPeriod.Items.Add(new ComboBoxItem { Content = "В этом месяце", Tag = "month" });
        cmbPeriod.SelectedIndex = 0;

        var cmbStatus = new ComboBox { Height = 34, Margin = new Thickness(8, 0, 0, 0) };
        cmbStatus.Items.Add(new ComboBoxItem { Content = "📌 Все статусы", Tag = "all" });
        cmbStatus.Items.Add(new ComboBoxItem { Content = "⭐ Ожидают оценки", Tag = "rate_needed" });
        cmbStatus.Items.Add(new ComboBoxItem { Content = "✨ Оценённые", Tag = "rated" });
        cmbStatus.Items.Add(new ComboBoxItem { Content = "🟡 В работе", Tag = "in_progress" });
        cmbStatus.Items.Add(new ComboBoxItem { Content = "🟢 Решённые и закрытые", Tag = "closed" });
        cmbStatus.SelectedIndex = 0;

        fRight.Children.Add(cmbPeriod);
        fRight.Children.Add(cmbStatus);

        DockPanel.SetDock(fRight, Dock.Right);
        filterDock.Children.Add(fRight);
        filterDock.Children.Add(searchBox);

        Grid.SetRow(filterDock, 0);
        grid.Children.Add(filterDock);

        // Cards list
        var cardsPanel = new StackPanel();
        var scroll = new ScrollViewer { Content = cardsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);

        void ApplyFilters()
        {
            cardsPanel.Children.Clear();
            string q = searchBox.Text.Trim().ToLowerInvariant();
            string period = (cmbPeriod.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
            string stFilter = (cmbStatus.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";

            var now = DateTime.Now;

            var filtered = myTickets.Where(t =>
            {
                if (!string.IsNullOrEmpty(q))
                {
                    bool match = t.GlpiId.ToString().Contains(q) ||
                                 t.Title.ToLowerInvariant().Contains(q) ||
                                 t.Room.ToLowerInvariant().Contains(q);
                    if (!match) return false;
                }

                if (period != "all")
                {
                    var dt = t.CreatedAt.ToLocalTime();
                    if (period == "today" && dt.Date != now.Date) return false;
                    if (period == "7days" && (now - dt).TotalDays > 7) return false;
                    if (period == "30days" && (now - dt).TotalDays > 30) return false;
                    if (period == "month" && (dt.Month != now.Month || dt.Year != now.Year)) return false;
                }

                if (stFilter != "all")
                {
                    if (stFilter == "rate_needed" && !t.CanRate) return false;
                    if (stFilter == "rated" && (!t.Score.HasValue || t.Score <= 0)) return false;
                    if (stFilter == "in_progress" && (t.Status == "5" || t.Status == "6")) return false;
                    if (stFilter == "closed" && t.Status != "5" && t.Status != "6") return false;
                }

                return true;
            }).ToList();

            if (filtered.Count == 0)
            {
                cardsPanel.Children.Add(new Border
                {
                    Background = Brushes.White,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(24),
                    Margin = new Thickness(0, 10, 0, 0),
                    Child = new TextBlock
                    {
                        Text = "Заявок не найдено.",
                        FontSize = 14,
                        Foreground = (Brush)new BrushConverter().ConvertFromString("#64748B")!,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                });
                return;
            }

            foreach (var ticket in filtered)
            {
                cardsPanel.Children.Add(BuildTicketRowCard(ticket));
            }
        }

        searchBox.TextChanged += (_, _) => ApplyFilters();
        cmbPeriod.SelectionChanged += (_, _) => ApplyFilters();
        cmbStatus.SelectionChanged += (_, _) => ApplyFilters();

        ApplyFilters();
        moduleHost.Content = grid;
    }

    private Border BuildTicketRowCard(UserTicketSummaryDto ticket)
    {
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#E2E8F0")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 10),
            Cursor = Cursors.Hand
        };

        var mainDock = new DockPanel();

        var btnOpen = new Button
        {
            Content = "Открыть карточку →",
            FontSize = 12.5,
            Padding = new Thickness(12, 6, 12, 6),
            Background = (Brush)new BrushConverter().ConvertFromString("#2563EB")!,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        btnOpen.Click += async (_, e) =>
        {
            e.Handled = true;
            await OpenTicketCardAsync(ticket.Id);
        };
        DockPanel.SetDock(btnOpen, Dock.Right);
        mainDock.Children.Add(btnOpen);

        var infoStack = new StackPanel();

        var topRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        topRow.Children.Add(new TextBlock
        {
            Text = $"#{ticket.GlpiId}",
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#2563EB")!,
            Margin = new Thickness(0, 0, 8, 0)
        });
        topRow.Children.Add(new TextBlock
        {
            Text = ticket.Title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#1E293B")!,
            Margin = new Thickness(0, 0, 10, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 420
        });
        topRow.Children.Add(BuildStatusBadge(ticket.Status, ticket.StatusName));

        if (ticket.CanRate)
        {
            var rateBadge = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString("#FEF3C7")!,
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#F59E0B")!,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(8, 0, 0, 0),
                Child = new TextBlock { Text = "⭐ Ожидает вашей оценки", FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = (Brush)new BrushConverter().ConvertFromString("#B45309")! }
            };
            topRow.Children.Add(rateBadge);
        }
        else if (ticket.Score.HasValue && ticket.Score.Value > 0)
        {
            var stars = new string('★', (int)Math.Round(ticket.Score.Value));
            var scoreBadge = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString("#FEF3C7")!,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(8, 0, 0, 0),
                Child = new TextBlock { Text = $"{stars} ({ticket.Score.Value:0.0})", FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = (Brush)new BrushConverter().ConvertFromString("#B45309")! }
            };
            topRow.Children.Add(scoreBadge);
        }

        infoStack.Children.Add(topRow);

        var metaRow = new TextBlock
        {
            Text = $"📅 {ticket.CreatedAt:dd.MM.yyyy HH:mm}   ·   🏢 {ticket.BranchName}   ·   🚪 Кабинет: {ticket.Room}   ·   👨‍🔧 Специалист: {ticket.AssignedSpecialist}",
            FontSize = 12,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#64748B")!,
            Margin = new Thickness(0, 0, 0, 4)
        };
        infoStack.Children.Add(metaRow);

        string snippet = ticket.Description.Length > 120 ? ticket.Description[..120] + "…" : ticket.Description;
        snippet = snippet.Replace("\r", " ").Replace("\n", " ");
        infoStack.Children.Add(new TextBlock
        {
            Text = snippet,
            FontSize = 12,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#475569")!
        });

        mainDock.Children.Add(infoStack);
        card.Child = mainDock;
        card.MouseLeftButtonUp += async (_, e) =>
        {
            if (e.Handled) return;
            await OpenTicketCardAsync(ticket.Id);
        };

        return card;
    }

    private static Border BuildStatusBadge(string status, string statusName)
    {
        string bg = "#E2E8F0";
        string fg = "#475569";
        int s = int.TryParse(status, out int n) ? n : 1;

        switch (s)
        {
            case 1: bg = "#DBEAFE"; fg = "#1D4ED8"; break;
            case 2:
            case 3: bg = "#FEF3C7"; fg = "#B45309"; break;
            case 4: bg = "#F3E8FF"; fg = "#7E22CE"; break;
            case 5: bg = "#DCFCE7"; fg = "#15803D"; break;
            case 6: bg = "#E2E8F0"; fg = "#334155"; break;
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
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)new BrushConverter().ConvertFromString(fg)!
        };
        return b;
    }

    #endregion

    #region 4. Module 1: Ticket Card Detail View

    private async Task OpenTicketCardAsync(int ticketId)
    {
        statusText.Text = "Загрузка карточки…";
        try
        {
            var detail = await hub.GetUserTicketDetailAsync(ticketId, lifetime.Token);
            if (detail == null)
            {
                MessageBox.Show("Не удалось загрузить данные заявки.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            RenderTicketDetailCard(detail);
            statusText.Text = $"Заявка #{detail.Ticket.GlpiId}";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка загрузки карточки: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            statusText.Text = "Ошибка";
        }
    }

    private void RenderTicketDetailCard(UserTicketDetailDto detail)
    {
        var ticket = detail.Ticket;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Top Back button
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Scroll body

        var btnBack = new Button
        {
            Content = "← Назад к списку моих заявок",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 6, 14, 6),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brushes.White,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!,
            Margin = new Thickness(0, 0, 0, 14)
        };
        btnBack.Click += (_, _) => RenderMyTicketsModule();
        Grid.SetRow(btnBack, 0);
        root.Children.Add(btnBack);

        var bodyStack = new StackPanel();
        var scroll = new ScrollViewer { Content = bodyStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        // 1. Title & Meta Card
        var mainCard = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 0, 14)
        };
        var mcStack = new StackPanel();

        var tRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        tRow.Children.Add(new TextBlock { Text = $"#{ticket.GlpiId} · {ticket.Title}", FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 10, 0) });
        tRow.Children.Add(BuildStatusBadge(ticket.Status, ticket.StatusName));
        mcStack.Children.Add(tRow);

        var metaText = new TextBlock
        {
            Text = $"🏢 Филиал: {ticket.BranchName}   ·   🚪 Кабинет: {ticket.Room}   ·   📅 Создано: {ticket.CreatedAt:dd.MM.yyyy HH:mm}\n👨‍🔧 Специалист: {ticket.AssignedSpecialist}",
            FontSize = 12.5,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#475569")!,
            LineHeight = 20,
            Margin = new Thickness(0, 0, 0, 12)
        };
        mcStack.Children.Add(metaText);

        mcStack.Children.Add(new TextBlock { Text = "Описание проблемы:", FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) });
        var descBox = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#F8FAFC")!,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#E2E8F0")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Child = new TextBlock { Text = ticket.Description, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 18 }
        };
        mcStack.Children.Add(descBox);

        mainCard.Child = mcStack;
        bodyStack.Children.Add(mainCard);

        // 2. Interactive Survey / Rating Card
        var surveyContainer = new ContentControl { Margin = new Thickness(0, 0, 0, 14) };
        bodyStack.Children.Add(surveyContainer);

        void RenderRatingBlock(double? currentScore, string? currentComment, bool canRateNow)
        {
            if (currentScore.HasValue && currentScore.Value > 0)
            {
                var ratedCard = new Border
                {
                    Background = (Brush)new BrushConverter().ConvertFromString("#FEF3C7")!,
                    BorderBrush = (Brush)new BrushConverter().ConvertFromString("#F59E0B")!,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16)
                };
                var rcStack = new StackPanel();
                rcStack.Children.Add(new TextBlock
                {
                    Text = $"⭐️ Ваша оценка работы специалиста: {new string('★', (int)Math.Round(currentScore.Value))} ({currentScore.Value:0.0} из 5)",
                    FontWeight = FontWeights.Bold,
                    FontSize = 14,
                    Foreground = (Brush)new BrushConverter().ConvertFromString("#92400E")!,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                if (!string.IsNullOrWhiteSpace(currentComment))
                {
                    rcStack.Children.Add(new TextBlock
                    {
                        Text = $"Ваш отзыв: «{currentComment}»",
                        FontSize = 12.5,
                        Foreground = (Brush)new BrushConverter().ConvertFromString("#78350F")!,
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                ratedCard.Child = rcStack;
                surveyContainer.Content = ratedCard;
            }
            else if (canRateNow)
            {
                var rateCard = new Border
                {
                    Background = (Brush)new BrushConverter().ConvertFromString("#FFFBEB")!,
                    BorderBrush = (Brush)new BrushConverter().ConvertFromString("#FBBF24")!,
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16)
                };
                var rcStack = new StackPanel();
                rcStack.Children.Add(new TextBlock
                {
                    Text = "⭐ Оцените результат решения вашей проблемы (в течение 15 дней после закрытия):",
                    FontWeight = FontWeights.Bold,
                    FontSize = 14,
                    Foreground = (Brush)new BrushConverter().ConvertFromString("#92400E")!,
                    Margin = new Thickness(0, 0, 0, 10)
                });

                // Interactive 5 stars buttons
                int selectedScore = 5;
                var starsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
                var starButtons = new List<Button>();

                void UpdateStarsVisual()
                {
                    for (int i = 0; i < starButtons.Count; i++)
                    {
                        starButtons[i].Foreground = (i < selectedScore)
                            ? (Brush)new BrushConverter().ConvertFromString("#F59E0B")!
                            : (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!;
                    }
                }

                for (int starIndex = 1; starIndex <= 5; starIndex++)
                {
                    int scoreVal = starIndex;
                    var sBtn = new Button
                    {
                        Content = "★",
                        FontSize = 26,
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Cursor = Cursors.Hand,
                        Margin = new Thickness(0, 0, 6, 0)
                    };
                    sBtn.Click += (_, _) =>
                    {
                        selectedScore = scoreVal;
                        UpdateStarsVisual();
                    };
                    starButtons.Add(sBtn);
                    starsRow.Children.Add(sBtn);
                }
                UpdateStarsVisual();
                rcStack.Children.Add(starsRow);

                var txtComment = new TextBox
                {
                    Height = 60,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12.5,
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 0, 0, 10),
                    ToolTip = "Ваш комментарий или пожелание специалисту (необязательно)…"
                };
                rcStack.Children.Add(txtComment);

                var btnSendRate = new Button
                {
                    Content = "⭐ Отправить оценку",
                    FontWeight = FontWeights.Bold,
                    FontSize = 13,
                    Background = (Brush)new BrushConverter().ConvertFromString("#D97706")!,
                    Foreground = Brushes.White,
                    Padding = new Thickness(16, 7, 16, 7),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                btnSendRate.Click += async (_, _) =>
                {
                    btnSendRate.IsEnabled = false;
                    btnSendRate.Content = "Отправка оценки…";
                    try
                    {
                        bool ok = await hub.RateUserTicketAsync(ticket.Id, selectedScore, txtComment.Text.Trim(), lifetime.Token);
                        if (ok)
                        {
                            MessageBox.Show("Большое спасибо! Ваша оценка успешно сохранена.", "Спасибо за отзыв", MessageBoxButton.OK, MessageBoxImage.Information);
                            RenderRatingBlock(selectedScore, txtComment.Text.Trim(), false);
                            await LoadMyTicketsAsync();
                        }
                        else
                        {
                            MessageBox.Show("Не удалось сохранить оценку. Возможно, срок оценки истёк.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Ошибка отправки оценки: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        btnSendRate.IsEnabled = true;
                        btnSendRate.Content = "⭐ Отправить оценку";
                    }
                };
                rcStack.Children.Add(btnSendRate);

                rateCard.Child = rcStack;
                surveyContainer.Content = rateCard;
            }
            else if (ticket.Status == "5" || ticket.Status == "6")
            {
                var expiredCard = new Border
                {
                    Background = (Brush)new BrushConverter().ConvertFromString("#F1F5F9")!,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12),
                    Child = new TextBlock
                    {
                        Text = "Оценка недоступна (заявка выполнена самостоятельно либо срок оценки 15 дней истёк).",
                        FontSize = 12,
                        Foreground = (Brush)new BrushConverter().ConvertFromString("#64748B")!
                    }
                };
                surveyContainer.Content = expiredCard;
            }
        }

        RenderRatingBlock(ticket.Score, ticket.ScoreComment, ticket.CanRate);

        // 3. Solutions
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
                    Padding = new Thickness(16),
                    Margin = new Thickness(0, 0, 0, 14)
                };
                var sStack = new StackPanel();
                sStack.Children.Add(new TextBlock
                {
                    Text = $"✅ Решение проблемы {(sol.Date.HasValue ? "(" + sol.Date.Value.ToString("dd.MM.yyyy HH:mm") + ")" : "")}:",
                    FontWeight = FontWeights.Bold,
                    FontSize = 13.5,
                    Foreground = (Brush)new BrushConverter().ConvertFromString("#166534")!,
                    Margin = new Thickness(0, 0, 0, 6)
                });
                sStack.Children.Add(new TextBlock { Text = sol.Content, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 18 });
                solCard.Child = sStack;
                bodyStack.Children.Add(solCard);
            }
        }

        // 4. Followups History
        var historyCard = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 14)
        };
        var hcStack = new StackPanel();

        hcStack.Children.Add(new TextBlock
        {
            Text = $"💬 Переписка со специалистами ({detail.Followups.Count})",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        });

        if (detail.Followups.Count == 0)
        {
            hcStack.Children.Add(new TextBlock
            {
                Text = "Ответов от специалистов пока нет. Ожидайте обработки обращения.",
                FontSize = 12.5,
                Foreground = (Brush)new BrushConverter().ConvertFromString("#64748B")!
            });
        }
        else
        {
            foreach (var f in detail.Followups)
            {
                var fBorder = new Border
                {
                    Background = (Brush)new BrushConverter().ConvertFromString("#F8FAFC")!,
                    BorderBrush = (Brush)new BrushConverter().ConvertFromString("#E2E8F0")!,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                var fStack = new StackPanel();

                var fHead = new DockPanel();
                fHead.Children.Add(new TextBlock { Text = $"👨‍🔧 {f.Author}", FontWeight = FontWeights.SemiBold, FontSize = 12.5 });
                if (f.Date.HasValue)
                {
                    var dt = new TextBlock { Text = f.Date.Value.ToString("dd.MM.yyyy HH:mm"), FontSize = 11.5, Foreground = (Brush)new BrushConverter().ConvertFromString("#94A3B8")! };
                    DockPanel.SetDock(dt, Dock.Right);
                    fHead.Children.Add(dt);
                }
                fStack.Children.Add(fHead);
                fStack.Children.Add(new TextBlock { Text = f.Content, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, LineHeight = 18, Margin = new Thickness(0, 6, 0, 0) });

                fBorder.Child = fStack;
                hcStack.Children.Add(fBorder);
            }
        }

        historyCard.Child = hcStack;
        bodyStack.Children.Add(historyCard);

        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        moduleHost.Content = root;
    }

    #endregion

    #region 5. Data Loading

    private async Task LoadTicketContextAsync()
    {
        try
        {
            var ctx = await hub.GetTicketContextAsync(lifetime.Token);
            if (ctx?.Branches != null)
            {
                branches = ctx.Branches.Where(b => b.IsActive).ToList();
                PopulateBranchesDropdown();
            }
        }
        catch { }
    }

    private async Task LoadMyTicketsAsync()
    {
        statusText.Text = "Загрузка заявок…";
        try
        {
            myTickets = await hub.GetUserTicketsAsync(lifetime.Token);
            btnTabMy.Content = $"📂 Мои заявки ({myTickets.Count})";
            statusText.Text = $"Обновлено в {DateTime.Now:HH:mm:ss}";

            if (activeTab == 1)
            {
                RenderMyTicketsModule();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            statusText.Text = "Ошибка: " + ex.Message;
        }
    }

    #endregion
}
