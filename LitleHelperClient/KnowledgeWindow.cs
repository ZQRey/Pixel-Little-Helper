using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PixelHelper;

public sealed record KnowledgeArticleDto(
    int Id,
    string Title,
    string Category,
    string ContentMarkdown,
    string Keywords,
    int? BranchId,
    string TargetRole,
    int ViewCount,
    bool IsPinned,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

internal sealed class KnowledgeWindow : Window
{
    private readonly HubConnectionService hub;
    private readonly Action<string> onOpenTicket;
    private readonly CancellationTokenSource cts = new();

    private readonly TextBox searchBox;
    private readonly StackPanel categoriesPanel;
    private readonly StackPanel articlesListPanel;
    private readonly ScrollViewer detailScrollViewer;
    private readonly StackPanel detailPanel;

    private List<KnowledgeArticleDto> allArticles = new();
    private string activeCategory = "Все";
    private KnowledgeArticleDto? selectedArticle;

    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    public KnowledgeWindow(HubConnectionService hub, Action<string> onOpenTicket)
    {
        this.hub = hub;
        this.onOpenTicket = onOpenTicket;

        Title = "База знаний и инструкции";
        Width = 760;
        Height = 560;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };

        var rootCard = new Border
        {
            Background = Brush("#F8FAFC"),
            BorderBrush = Brush("#CBD5E1"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(12),
            Effect = new DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 4,
                Opacity = 0.22,
                Color = Color.FromRgb(15, 23, 42)
            }
        };

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Categories
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content (Split: List | Detail)
        rootCard.Child = mainGrid;
        Content = rootCard;

        // Header
        var headerBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E2E8F0"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(16, 16, 0, 0),
            Padding = new Thickness(18, 12, 14, 12)
        };
        headerBorder.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };

        var headerDock = new DockPanel();
        var closeBtn = new Button
        {
            Content = "✕",
            Width = 30,
            Height = 30,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brush("#64748B"),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Cursor = Cursors.Hand
        };
        closeBtn.Click += (_, _) => Close();
        DockPanel.SetDock(closeBtn, Dock.Right);
        headerDock.Children.Add(closeBtn);

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal };
        var iconTxt = new TextBlock
        {
            Text = "📚",
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var titleTxt = new TextBlock
        {
            Text = "База знаний и инструкции",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#0F172A"),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(iconTxt);
        titleStack.Children.Add(titleTxt);
        headerDock.Children.Add(titleStack);
        headerBorder.Child = headerDock;
        Grid.SetRow(headerBorder, 0);
        mainGrid.Children.Add(headerBorder);

        // Search & Category Filters Bar
        var filterBar = new StackPanel
        {
            Margin = new Thickness(18, 10, 18, 6)
        };

        searchBox = new TextBox
        {
            Height = 36,
            Padding = new Thickness(10, 6, 10, 6),
            FontSize = 13,
            Background = Brushes.White,
            BorderBrush = Brush("#CBD5E1"),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        searchBox.TextChanged += (_, _) => ApplyFilter();
        filterBar.Children.Add(searchBox);

        categoriesPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4)
        };
        filterBar.Children.Add(categoriesPanel);

        Grid.SetRow(filterBar, 1);
        mainGrid.Children.Add(filterBar);

        // Content Area (Two Columns: Article List and Article Details)
        var contentGrid = new Grid { Margin = new Thickness(18, 4, 18, 14) };
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); // spacing
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left: Article List
        var listScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        articlesListPanel = new StackPanel();
        listScroll.Content = articlesListPanel;

        var listBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E2E8F0"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6),
            Child = listScroll
        };
        Grid.SetColumn(listBorder, 0);
        contentGrid.Children.Add(listBorder);

        // Right: Article Detail
        detailScrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        detailPanel = new StackPanel { Margin = new Thickness(8) };
        detailScrollViewer.Content = detailPanel;

        var detailBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E2E8F0"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = detailScrollViewer
        };
        Grid.SetColumn(detailBorder, 2);
        contentGrid.Children.Add(detailBorder);

        Grid.SetRow(contentGrid, 2);
        mainGrid.Children.Add(contentGrid);

        Loaded += async (_, _) => await LoadArticlesAsync();
        Closed += (_, _) => cts.Cancel();
    }

    private async Task LoadArticlesAsync()
    {
        articlesListPanel.Children.Clear();
        articlesListPanel.Children.Add(new TextBlock
        {
            Text = "Загрузка статей...",
            Foreground = Brush("#64748B"),
            Margin = new Thickness(10)
        });

        try
        {
            if (hub.IsOnline)
            {
                allArticles = await hub.GetKnowledgeArticlesAsync(token: cts.Token);
                Title = "База знаний — статьи сервера";
            }
            else
            {
                Title = "База знаний — автономные статьи (сервер недоступен)";
                allArticles = GetOfflineFallbackArticles();
            }
        }
        catch
        {
            Title = "База знаний — автономные статьи (сервер недоступен)";
                allArticles = GetOfflineFallbackArticles();
        }

        RebuildCategoryButtons();
        ApplyFilter();
    }

    private void RebuildCategoryButtons()
    {
        categoriesPanel.Children.Clear();
        var cats = new List<string> { "Все" };
        var uniqueCats = allArticles.Select(a => a.Category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c);
        cats.AddRange(uniqueCats);

        foreach (var cat in cats)
        {
            bool isActive = string.Equals(cat, activeCategory, StringComparison.OrdinalIgnoreCase);
            var btn = new Button
            {
                Content = cat,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 0),
                Background = isActive ? Brush("#0284C7") : Brush("#E2E8F0"),
                Foreground = isActive ? Brushes.White : Brush("#334155"),
                FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal,
                FontSize = 11,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            btn.Click += (_, _) =>
            {
                activeCategory = cat;
                RebuildCategoryButtons();
                ApplyFilter();
            };
            categoriesPanel.Children.Add(btn);
        }
    }

    private void ApplyFilter()
    {
        string term = (searchBox.Text ?? "").Trim().ToLowerInvariant();
        var filtered = allArticles.Where(a =>
        {
            bool matchCat = activeCategory == "Все" || string.Equals(a.Category, activeCategory, StringComparison.OrdinalIgnoreCase);
            if (!matchCat) return false;
            if (string.IsNullOrEmpty(term)) return true;
            return a.Title.ToLowerInvariant().Contains(term) ||
                   a.Keywords.ToLowerInvariant().Contains(term) ||
                   a.ContentMarkdown.ToLowerInvariant().Contains(term);
        }).ToList();

        articlesListPanel.Children.Clear();
        if (filtered.Count == 0)
        {
            articlesListPanel.Children.Add(new TextBlock
            {
                Text = "Статьи не найдены",
                Foreground = Brush("#94A3B8"),
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(10)
            });
            ShowEmptyDetail();
            return;
        }

        foreach (var art in filtered)
        {
            bool isSelected = selectedArticle?.Id == art.Id;
            var itemBorder = new Border
            {
                Background = isSelected ? Brush("#E0F2FE") : Brushes.Transparent,
                BorderBrush = isSelected ? Brush("#38BDF8") : Brush("#F1F5F9"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand
            };

            var stack = new StackPanel();
            var title = new TextBlock
            {
                Text = art.Title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = Brush("#0F172A"),
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(title);

            var catLabel = new TextBlock
            {
                Text = art.Category,
                FontSize = 10,
                Foreground = Brush("#0284C7"),
                Margin = new Thickness(0, 3, 0, 0)
            };
            stack.Children.Add(catLabel);

            itemBorder.Child = stack;
            itemBorder.MouseLeftButtonDown += (_, _) =>
            {
                selectedArticle = art;
                ApplyFilter();
                ShowArticleDetail(art);
            };

            articlesListPanel.Children.Add(itemBorder);
        }

        if (selectedArticle == null || !filtered.Any(a => a.Id == selectedArticle.Id))
        {
            selectedArticle = filtered[0];
            ShowArticleDetail(selectedArticle);
        }
    }

    private void ShowArticleDetail(KnowledgeArticleDto art)
    {
        detailPanel.Children.Clear();

        // Title
        var title = new TextBlock
        {
            Text = art.Title,
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#0F172A"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        };
        detailPanel.Children.Add(title);

        // Category Badge
        var badge = new Border
        {
            Background = Brush("#E0F2FE"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 14),
            Child = new TextBlock
            {
                Text = $"Категория: {art.Category}",
                FontSize = 11,
                Foreground = Brush("#0369A1"),
                FontWeight = FontWeights.Medium
            }
        };
        detailPanel.Children.Add(badge);

        // Content (Formatted Markdown Rendering)
        RenderMarkdown(art.ContentMarkdown, detailPanel);

        // Divider
        detailPanel.Children.Add(new Border
        {
            Height = 1,
            Background = Brush("#E2E8F0"),
            Margin = new Thickness(0, 18, 0, 14)
        });

        // "Did not help? Open ticket" button
        var actionPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ticketBtn = new Button
        {
            Content = "🛠️ Не помогло? Создать заявку в ИТ",
            Padding = new Thickness(14, 8, 14, 8),
            Background = Brush("#0284C7"),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        ticketBtn.Click += (_, _) =>
        {
            string prefilled = $"[База знаний: {art.Title}]\n\nПроблема не решена стандартной инструкцией:\n";
            Close();
            onOpenTicket(prefilled);
        };
        actionPanel.Children.Add(ticketBtn);
        detailPanel.Children.Add(actionPanel);
    }

    private void RenderMarkdown(string markdown, StackPanel container)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return;
        var lines = markdown.Split('\n');
        foreach (var rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                container.Children.Add(new TextBlock { Height = 6 });
                continue;
            }

            if (line.StartsWith("### "))
            {
                container.Children.Add(new TextBlock
                {
                    Text = line[4..],
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brush("#1E293B"),
                    Margin = new Thickness(0, 8, 0, 4)
                });
            }
            else if (line.StartsWith("#### "))
            {
                container.Children.Add(new TextBlock
                {
                    Text = line[5..],
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brush("#334155"),
                    Margin = new Thickness(0, 6, 0, 3)
                });
            }
            else if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                var bulletStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 2, 0, 2) };
                bulletStack.Children.Add(new TextBlock { Text = "• ", Foreground = Brush("#0284C7"), FontWeight = FontWeights.Bold });
                bulletStack.Children.Add(new TextBlock
                {
                    Text = line[2..].Replace("**", ""),
                    FontSize = 12,
                    Foreground = Brush("#475569"),
                    TextWrapping = TextWrapping.Wrap
                });
                container.Children.Add(bulletStack);
            }
            else if (char.IsDigit(line[0]) && line.Length > 2 && line[1] == '.')
            {
                var numStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 2, 0, 2) };
                numStack.Children.Add(new TextBlock { Text = line[..3] + " ", Foreground = Brush("#0284C7"), FontWeight = FontWeights.Bold });
                numStack.Children.Add(new TextBlock
                {
                    Text = line[3..].Replace("**", ""),
                    FontSize = 12,
                    Foreground = Brush("#475569"),
                    TextWrapping = TextWrapping.Wrap
                });
                container.Children.Add(numStack);
            }
            else
            {
                container.Children.Add(new TextBlock
                {
                    Text = line.Replace("**", ""),
                    FontSize = 12,
                    Foreground = Brush("#334155"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 2),
                    LineHeight = 18
                });
            }
        }
    }

    private void ShowEmptyDetail()
    {
        detailPanel.Children.Clear();
        detailPanel.Children.Add(new TextBlock
        {
            Text = "Выберите статью из списка слева для просмотра инструкции",
            Foreground = Brush("#94A3B8"),
            FontStyle = FontStyles.Italic,
            Margin = new Thickness(20)
        });
    }

    private static List<KnowledgeArticleDto> GetOfflineFallbackArticles() =>
    [
        new(1, "Запуск и решение проблем с NCALayer", "NCALayer",
            "### Работа с NCALayer\n\n1. Убедитесь, что значок NCALayer виден возле часов.\n2. При отсутствии запустите NCALayer с Рабочего стола.\n3. Перезагрузите страницу клавишами Ctrl+F5.",
            "ncalayer, эцп", null, "All", 0, true, DateTime.UtcNow, DateTime.UtcNow),
        new(2, "Вход и работа с медицинской системой DMED", "DMED",
            "### Портал DMED (krg.dmed.kz)\n\n1. Перейдите по ссылке krg.dmed.kz.\n2. Введите ИИН и пароль сотрудника.\n3. При подписании приёма выберите сертификат ЭЦП.",
            "dmed, вход", null, "All", 0, true, DateTime.UtcNow, DateTime.UtcNow),
        new(3, "Что делать при замятии бумаги или остановке печати", "Принтеры и картриджи",
            "### Решение проблем печати\n\n1. Проверьте лоток бумаги.\n2. В меню Артёмки выберите «Очистить очередь печати».\n3. Перезагрузите принтер кнопкой питания.",
            "принтер, печать, очередь", null, "All", 0, false, DateTime.UtcNow, DateTime.UtcNow),
        new(4, "Установка и продление сертификатов ЭЦП НУЦ РК", "ЭЦП",
            "### Сертификаты ЭЦП (pki.gov.kz)\n\n1. Продлите ключ в Личном кабинете pki.gov.kz до истечения срока.\n2. Импортируйте p12 файл через меню Артёмки.",
            "эцп, pki.gov.kz", null, "All", 0, false, DateTime.UtcNow, DateTime.UtcNow)
    ];
}
