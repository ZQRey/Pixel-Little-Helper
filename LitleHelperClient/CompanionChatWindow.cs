using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PixelHelper;

internal sealed class CompanionChatWindow : Window
{
    private readonly Settings settings;
    private readonly Action<PetState, int>? onRobotReact;
    private readonly StackPanel messagesPanel = new();
    private readonly ScrollViewer scrollViewer = new();
    private readonly TextBox inputTextBox = new();
    private readonly Border typingIndicator = new();
    private readonly TextBlock userTypingIndicator = new();
    private readonly Image headerAvatar = new();
    private readonly Sprites? sprites;
    private bool isBotTyping = false;
    private bool isAwaitingPassword = false;

    public CompanionChatWindow(Settings settings, Sprites? sprites = null, Action<PetState, int>? onRobotReact = null)
    {
        this.settings = settings;
        this.sprites = sprites;
        this.onRobotReact = onRobotReact;

        Title = Loc.T("CompanionWindowTitle");
        Width = 440;
        Height = 580;
        MinWidth = 360;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)); // Slate 900
        Foreground = Brushes.White;

        Content = BuildUI();

        Loaded += (_, _) =>
        {
            CompanionStorage.AutoPurge();
            LoadHistory();
            inputTextBox.Focus();
        };
    }

    private UIElement BuildUI()
    {
        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Chat history
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Quick chips
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Input row

        // --- Header ---
        var headerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)), // Slate 800
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 10, 12, 10)
        };
        headerBorder.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Avatar
        headerAvatar.Width = 32;
        headerAvatar.Height = 32;
        headerAvatar.Margin = new Thickness(0, 0, 10, 0);
        RenderOptions.SetBitmapScalingMode(headerAvatar, BitmapScalingMode.NearestNeighbor);
        UpdateAvatarState(PetState.Idle);
        Grid.SetColumn(headerAvatar, 0);
        headerGrid.Children.Add(headerAvatar);

        // Titles
        var titlesStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleText = new TextBlock
        {
            Text = Loc.T("CompanionWindowTitle"),
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Foreground = Brushes.White
        };
        var statusText = new TextBlock
        {
            Text = Loc.T("CompanionBotStatus"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)) // Slate 400
        };
        titlesStack.Children.Add(titleText);
        titlesStack.Children.Add(statusText);
        Grid.SetColumn(titlesStack, 1);
        headerGrid.Children.Add(titlesStack);

        // Header buttons: Clean & Close
        var btnStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var clearBtn = new Button
        {
            Content = "🧹",
            ToolTip = Loc.T("ClearChatHistory"),
            Width = 32,
            Height = 32,
            Margin = new Thickness(0, 0, 6, 0),
            Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        clearBtn.Click += (_, _) => PromptClearHistory();
        btnStack.Children.Add(clearBtn);

        var closeBtn = new Button
        {
            Content = "✕",
            Width = 32,
            Height = 32,
            Background = new SolidColorBrush(Color.FromRgb(225, 29, 72)), // Rose 600
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.Bold,
            Cursor = Cursors.Hand
        };
        closeBtn.Click += (_, _) => Close();
        btnStack.Children.Add(closeBtn);

        Grid.SetColumn(btnStack, 2);
        headerGrid.Children.Add(btnStack);

        headerBorder.Child = headerGrid;
        Grid.SetRow(headerBorder, 0);
        mainGrid.Children.Add(headerBorder);

        // --- Chat Area ---
        scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        scrollViewer.Padding = new Thickness(12, 12, 12, 12);

        messagesPanel.Orientation = Orientation.Vertical;
        scrollViewer.Content = messagesPanel;
        Grid.SetRow(scrollViewer, 1);
        mainGrid.Children.Add(scrollViewer);

        // Typing indicator
        typingIndicator.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
        typingIndicator.CornerRadius = new CornerRadius(12);
        typingIndicator.Padding = new Thickness(10, 6, 10, 6);
        typingIndicator.Margin = new Thickness(4, 4, 4, 8);
        typingIndicator.HorizontalAlignment = HorizontalAlignment.Left;
        typingIndicator.Visibility = Visibility.Collapsed;
        var typingText = new TextBlock
        {
            Text = Loc.T("TypingPsychologist"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184))
        };
        typingIndicator.Child = typingText;
        messagesPanel.Children.Add(typingIndicator);

        // --- Quick Chips ---
        var chipsBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            Padding = new Thickness(10, 4, 10, 4)
        };
        string currentLang = Loc.Code ?? "ru";
        var chipsWrap = new WrapPanel { Orientation = Orientation.Horizontal };
        var (cSad, pSad) = currentLang switch
        {
            "kk" => ("Көңіл-күйім жоқ 😢", "Маған өте қиын, көңіл-күйім жоқ..."),
            "en" => ("Feeling down 😢", "I'm feeling down and sad..."),
            "zh" => ("心情不好 😢", "我今天心情有点低落..."),
            _ => ("Мне грустно 😢", "Мне грустно...")
        };
        var (cTired, pTired) = currentLang switch
        {
            "kk" => ("Жұмыста шаршадым 😫", "Жұмыстан қатты шаршадым"),
            "en" => ("Hard day 😫", "Very hard day at work, exhausted"),
            "zh" => ("工作好累 😫", "工作压力好大，太累了"),
            _ => ("Тяжело на работе 😫", "Очень тяжело на работе, устал")
        };
        var (cPass, pPass) = currentLang switch
        {
            "kk" => ("Құпия сөз 🔑", "Құпия сөзді қалай ауыстырамын?"),
            "en" => ("Password 🔑", "How to change password?"),
            "zh" => ("修改密码 🔑", "如何修改密码？"),
            _ => ("Сменить пароль 🔑", "Как поменять пароль?")
        };
        var (cJoke, pJoke) = currentLang switch
        {
            "kk" => ("Әзіл айтшы 🎭", "Маған әзіл айтып берші"),
            "en" => ("Tell a joke 🎭", "Tell me a joke"),
            "zh" => ("讲个笑话 🎭", "给我讲个笑话吧"),
            _ => ("Расскажи шутку 🎭", "Расскажи шутку")
        };
        var (cSuccess, pSuccess) = currentLang switch
        {
            "kk" => ("Сәтті өтті! 🎉", "Менде бәрі сәтті өтті!"),
            "en" => ("I did it! 🎉", "Everything worked out!"),
            "zh" => ("成功了！🎉", "我终于成功了！"),
            _ => ("У меня получилось! 🎉", "У меня всё получилось!")
        };
        var (cWho, pWho) = currentLang switch
        {
            "kk" => ("Сен кімсің? 🤖", "Сен кімсің?"),
            "en" => ("Who are you? 🤖", "Who are you?"),
            "zh" => ("你是谁？🤖", "你是谁？"),
            _ => ("Кто ты? 🤖", "Кто ты?")
        };

        AddChip(chipsWrap, cSad, pSad);
        AddChip(chipsWrap, cTired, pTired);
        AddChip(chipsWrap, cPass, pPass);
        AddChip(chipsWrap, cJoke, pJoke);
        AddChip(chipsWrap, cSuccess, pSuccess);
        AddChip(chipsWrap, cWho, pWho);
        chipsBorder.Child = chipsWrap;
        Grid.SetRow(chipsBorder, 2);
        mainGrid.Children.Add(chipsBorder);

        // --- Input Row ---
        var inputBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 10, 10, 10)
        };
        var inputGrid = new Grid();
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        inputTextBox.Background = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        inputTextBox.Foreground = Brushes.White;
        inputTextBox.CaretBrush = Brushes.White;
        inputTextBox.BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105));
        inputTextBox.BorderThickness = new Thickness(1);
        inputTextBox.Padding = new Thickness(10, 8, 10, 8);
        inputTextBox.FontSize = 13;
        inputTextBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                e.Handled = true;
                SendUserMessage();
            }
        };
        Grid.SetColumn(inputTextBox, 0);
        inputGrid.Children.Add(inputTextBox);

        var sendBtn = new Button
        {
            Content = "➤",
            FontSize = 14,
            Width = 42,
            Height = 36,
            Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(59, 130, 246)), // Blue 500
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        sendBtn.Click += (_, _) => SendUserMessage();
        Grid.SetColumn(sendBtn, 1);
        inputGrid.Children.Add(sendBtn);

        userTypingIndicator.Text = Loc.T("TypingEmployeeText");
        userTypingIndicator.FontSize = 11;
        userTypingIndicator.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
        userTypingIndicator.Margin = new Thickness(4, 0, 0, 6);
        userTypingIndicator.Visibility = Visibility.Collapsed;

        inputTextBox.TextChanged += (_, _) =>
        {
            userTypingIndicator.Text = Loc.T("TypingEmployeeText");
            userTypingIndicator.Visibility = string.IsNullOrWhiteSpace(inputTextBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        };

        var inputStack = new StackPanel();
        inputStack.Children.Add(userTypingIndicator);
        inputStack.Children.Add(inputGrid);

        inputBorder.Child = inputStack;
        Grid.SetRow(inputBorder, 3);
        mainGrid.Children.Add(inputBorder);

        return mainGrid;
    }

    private void AddChip(WrapPanel panel, string label, string prompt)
    {
        var chip = new Button
        {
            Content = label,
            FontSize = 11,
            Margin = new Thickness(2, 2, 2, 2),
            Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        chip.Click += (_, _) =>
        {
            inputTextBox.Text = prompt;
            SendUserMessage();
        };
        panel.Children.Add(chip);
    }

    private void UpdateAvatarState(PetState state)
    {
        if (sprites != null)
        {
            try
            {
                var frame = sprites.Get(state, 0);
                headerAvatar.Source = frame.Image;
            }
            catch { }
        }
    }

    private void LoadHistory()
    {
        messagesPanel.Children.Clear();
        messagesPanel.Children.Add(typingIndicator);

        var history = CompanionStorage.Load();
        if (history.Count == 0)
        {
            string greeting = Loc.Code switch
            {
                "kk" => "Сәлем! Мен сенің пиксельді досыңмын. Бүгінгі күнің қалай өтуде? Бірге сөйлесейік! ✨",
                "en" => "Hello! I am your pixel companion. How is your day going? I'm always here to listen! ✨",
                "zh" => "你好！我是你的像素小助手伙伴。今天过得怎么样？无论开心还是烦恼，都可以跟我说哦！✨",
                _ => "Привет! Я твой пиксельный друг. Как проходит твой день? Я всегда готов выслушать тебя и поддержать! ✨"
            };
            AddBotBubble(greeting, DateTime.UtcNow);
        }
        else
        {
            foreach (var msg in history)
            {
                if (msg.IsUser)
                    AddUserBubble(msg.Text, msg.Timestamp);
                else
                    AddBotBubble(msg.Text, msg.Timestamp);
            }
        }
        ScrollToBottom();
    }

    private void PromptClearHistory()
    {
        var result = MessageBox.Show(
            Loc.Code switch
            {
                "kk" => "Барлық хат-хабарды өшіруді қалайсыз ба?",
                "en" => "Are you sure you want to erase all chat history?",
                "zh" => "您确定要清空所有聊天记录吗？",
                _ => "Вы уверены, что хотите полностью стереть историю переписки?"
            },
            Loc.T("CompanionWindowTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );

        if (result == MessageBoxResult.Yes)
        {
            CompanionStorage.Clear();
            LoadHistory();
            onRobotReact?.Invoke(PetState.Joy, 2);
        }
    }

    private async void SendUserMessage()
    {
        if (isBotTyping) return;
        string text = inputTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        inputTextBox.Text = "";
        userTypingIndicator.Visibility = Visibility.Collapsed;
        AddUserBubble(text, DateTime.UtcNow);
        CompanionStorage.Append(new CompanionMessage(DateTime.UtcNow, true, text));
        ScrollToBottom();

        isBotTyping = true;
        typingIndicator.Visibility = Visibility.Visible;
        messagesPanel.Children.Remove(typingIndicator);
        messagesPanel.Children.Add(typingIndicator);
        ScrollToBottom();

        // Natural typing delay (600-1100ms)
        await Task.Delay(800);

        var response = CompanionBotEngine.GenerateResponse(text, settings.FirstStartupDate, isAwaitingPassword);
        isAwaitingPassword = response.IsPromptingPassword;

        typingIndicator.Visibility = Visibility.Collapsed;
        isBotTyping = false;

        if (response.Action == CompanionBotAction.ClearHistory)
        {
            messagesPanel.Children.Clear();
            messagesPanel.Children.Add(typingIndicator);
        }

        AddBotBubble(response.ReplyText, DateTime.UtcNow);
        if (response.Action != CompanionBotAction.ClearHistory)
        {
            CompanionStorage.Append(new CompanionMessage(DateTime.UtcNow, false, response.ReplyText, response.Emotion.ToString()));
        }

        UpdateAvatarState(response.RobotState);
        onRobotReact?.Invoke(response.RobotState, 4);

        ScrollToBottom();

        if (response.Action == CompanionBotAction.ChangePassword && !string.IsNullOrWhiteSpace(response.TargetPassword))
        {
            await ExecutePasswordChangeAsync(response.TargetPassword);
        }
    }

    private async Task ExecutePasswordChangeAsync(string targetPassword)
    {
        isBotTyping = true;
        typingIndicator.Visibility = Visibility.Visible;
        messagesPanel.Children.Remove(typingIndicator);
        messagesPanel.Children.Add(typingIndicator);
        ScrollToBottom();

        bool ok = false;
        string? error = null;

        try
        {
            using var api = new ApiClient(settings);
            (ok, error) = await api.ChangeAdPasswordAsync(Environment.UserName, targetPassword);
        }
        catch (Exception ex)
        {
            ok = false;
            error = ex.Message;
        }

        typingIndicator.Visibility = Visibility.Collapsed;
        isBotTyping = false;

        if (ok)
        {
            string successText = Loc.T("PasswordChangeSuccess");
            AddBotBubble(successText, DateTime.UtcNow);
            CompanionStorage.Append(new CompanionMessage(DateTime.UtcNow, false, successText, CompanionEmotion.Joy.ToString()));
            UpdateAvatarState(PetState.Celebrate);
            onRobotReact?.Invoke(PetState.Celebrate, 5);
        }
        else
        {
            string failText = Loc.Format("PasswordChangeFailed", error ?? "Unknown error");
            AddBotBubble(failText, DateTime.UtcNow);
            CompanionStorage.Append(new CompanionMessage(DateTime.UtcNow, false, failText, CompanionEmotion.Sad.ToString()));
            UpdateAvatarState(PetState.Sad);
            onRobotReact?.Invoke(PetState.Sad, 4);
        }

        ScrollToBottom();
    }

    private void AddUserBubble(string text, DateTime timestamp)
    {
        var container = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)), // Blue 600
            CornerRadius = new CornerRadius(14, 14, 2, 14),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(40, 4, 4, 4),
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var stack = new StackPanel();
        var txt = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            FontSize = 13
        };
        var time = new TextBlock
        {
            Text = timestamp.ToLocalTime().ToString("HH:mm"),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(191, 219, 254)),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 2, 0, 0)
        };
        stack.Children.Add(txt);
        stack.Children.Add(time);
        container.Child = stack;

        // Insert before typing indicator
        int idx = messagesPanel.Children.IndexOf(typingIndicator);
        if (idx >= 0) messagesPanel.Children.Insert(idx, container);
        else messagesPanel.Children.Add(container);
    }

    private void AddBotBubble(string text, DateTime timestamp)
    {
        var container = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)), // Slate 800
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14, 14, 14, 2),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(4, 4, 40, 4),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var stack = new StackPanel();
        var txt = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
            FontSize = 13
        };
        var time = new TextBlock
        {
            Text = timestamp.ToLocalTime().ToString("HH:mm"),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 2, 0, 0)
        };
        stack.Children.Add(txt);
        stack.Children.Add(time);
        container.Child = stack;

        int idx = messagesPanel.Children.IndexOf(typingIndicator);
        if (idx >= 0) messagesPanel.Children.Insert(idx, container);
        else messagesPanel.Children.Add(container);
    }

    private void ScrollToBottom()
    {
        Dispatcher.InvokeAsync(() => scrollViewer.ScrollToEnd(), DispatcherPriority.Loaded);
    }
}
