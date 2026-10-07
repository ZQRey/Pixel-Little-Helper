using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Markup;
using System.IO;
using System.Windows.Media.Imaging;

namespace PixelHelper;
internal sealed class MessengerWindow : Window
{
    private readonly MessengerClient client;
    private readonly Settings settings;
    private readonly Grid root = new() { Margin = new Thickness(12) };
    private Brush surface = Brushes.White, ink = Brushes.Black, muted = Brushes.Gray, incoming = Brushes.White, outgoing = Brushes.LightCyan, accent = Brushes.Teal;
    private void ApplyTheme()
    {
        bool light = settings.ChatTheme == "Light", contrast = settings.ChatTheme == "Contrast";
        Background=MessengerDesign.Brush(settings.ChatTheme,"base");surface=MessengerDesign.Brush(settings.ChatTheme,"raised");ink=MessengerDesign.Brush(settings.ChatTheme,"ink");muted=MessengerDesign.Brush(settings.ChatTheme,"muted");incoming=MessengerDesign.Brush(settings.ChatTheme,"incoming");accent=MessengerDesign.Brush(settings.ChatTheme,"accent");outgoing=contrast?Brushes.Black:new LinearGradientBrush(System.Windows.Media.Color.FromRgb(99,102,241),System.Windows.Media.Color.FromRgb(139,92,246),45);
        Foreground = ink; FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"); FontSize = Math.Clamp(settings.ChatFontSize, 11, 22);
        var textStyle = new Style(typeof(TextBox)); textStyle.Setters.Add(new Setter(Control.BackgroundProperty, surface)); textStyle.Setters.Add(new Setter(Control.ForegroundProperty, ink)); textStyle.Setters.Add(new Setter(Control.BorderBrushProperty, muted)); textStyle.Setters.Add(new Setter(TextBox.CaretBrushProperty, ink)); textStyle.Setters.Add(new Setter(Control.FontSizeProperty, FontSize)); Resources[typeof(TextBox)] = textStyle;
        var checkStyle = new Style(typeof(CheckBox)); checkStyle.Setters.Add(new Setter(Control.ForegroundProperty, ink)); Resources[typeof(CheckBox)] = checkStyle;
        Resources.MergedDictionaries.Clear(); Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/PixelHelper;component/MessengerControls.xaml",UriKind.Relative) });
    }
    private Brush ChatBackdrop()
    {
        if (settings.ChatBackground == "Image" && File.Exists(settings.ChatBackgroundImage))
        {
            try { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.UriSource = new Uri(settings.ChatBackgroundImage!, UriKind.Absolute); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 1600; bitmap.EndInit(); bitmap.Freeze(); return new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill, Opacity = 1 - Math.Clamp(settings.ChatBackgroundDim, 0, .85) }; }
            catch (Exception ex) { Settings.Log(ex); }
        }
        if (settings.ChatBackground == "Dots" || settings.ChatBackground == "Grid")
        {
            var group = new DrawingGroup(); var pen = new Pen(muted, .5);
            group.Children.Add(settings.ChatBackground == "Dots" ? new GeometryDrawing(muted, null, new EllipseGeometry(new Point(2, 2), 1, 1)) : new GeometryDrawing(null, pen, Geometry.Parse("M0,24 L0,0 24,0")));
            return new DrawingBrush(group) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 24, 24), ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 24, 24), Opacity = .16 };
        }
        return Brushes.Transparent;
    }
    private List<ChatContact> contacts = [];
    private List<ChatEntry> messages = [];
    private ListBox? users;
    private TextBox? search, input;
    private TextBlock? error, title;
    private StackPanel? history;
    private ScrollViewer? scroll;
    private Button? send;
    private int peer;
    private bool updating, closing, showingLogin;
    private string? pendingId, pendingText;
    private int pendingPeer;
    private readonly List<string> pendingFiles = [];
    private readonly string clipboardDirectory = Path.Combine(Path.GetTempPath(), "PixelHelper-chat-" + Guid.NewGuid().ToString("N"));
    private WrapPanel? attachmentsPanel;
    private bool sending;
    private ChatEntry? pendingMessage;
    private bool pendingFailed;
    private StackPanel? details;
    private bool detailsOpen;
    private Dictionary<string,List<ReactionInfo>> reactions=[];
    private string chatFilter = "all";
    internal event Action<HelperEmoji>? EmojiInserted;
    internal int ActivePeer => IsActive ? peer : 0;
    internal MessengerWindow(MessengerClient client, Settings settings)
    {
        this.client = client; this.settings = settings;
        Title = "Мессенджер · PixelHelper"; Width = 1240; Height = 780; MinWidth = 820; WindowStyle = WindowStyle.None; System.Windows.Shell.WindowChrome.SetWindowChrome(this,new System.Windows.Shell.WindowChrome { CaptionHeight=0,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(16) }); MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Content = root; ApplyTheme();
        client.Changed += OnChanged;
        client.MessageReceived += OnMessage;
        client.TypingReceived += OnTyping;
        Loaded += async (_, _) => await RefreshAsync();
        Activated += async (_, _) => await MarkReadAsync();
        Closed += (_, _) => { closing = true; client.Changed -= OnChanged; client.MessageReceived -= OnMessage; client.TypingReceived -= OnTyping; try { if (Directory.Exists(clipboardDirectory)) Directory.Delete(clipboardDirectory, true); } catch (IOException ex) { Settings.Log(ex); } };
    }
    private void OnChanged() => Dispatcher.BeginInvoke(new Action(async () => { if (!closing) await RefreshAsync(); }));
    private void OnMessage(ChatEntry message) => OnChanged();
    private DateTime typingSent;
    private void OnTyping(int id,string name)=>Dispatcher.BeginInvoke(new Action(async()=>{if(id!=peer||title==null)return;title.Text=(contacts.FirstOrDefault(c=>c.Id==peer)?.FullName??"")+" · "+name+" печатает…";await Task.Delay(5000);if(!closing&&id==peer&&title!=null)title.Text=contacts.FirstOrDefault(c=>c.Id==peer)?.FullName??"Диалог";}));
    private Button Button(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text, Background = surface, Foreground = ink, BorderBrush = accent, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(3) };
        button.Click += click; return button;
    }
    private void LoginForm()
    {
        showingLogin = true; users = null; peer = 0; contacts.Clear(); messages.Clear(); pendingId = null;
        root.Children.Clear(); root.ColumnDefinitions.Clear(); root.RowDefinitions.Clear();
        var panel = new StackPanel { Width = 360, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = "Автоматический вход Windows", FontSize = 22, Margin = new Thickness(0, 0, 0, 20) });
        panel.Children.Add(new TextBlock { Text = Environment.UserDomainName + "\\" + Environment.UserName, Margin = new Thickness(0, 0, 0, 12) });
        error = new TextBlock { Foreground = muted, TextWrapping = TextWrapping.Wrap, Text = client.SignInStatus }; panel.Children.Add(error);
        var submit = Button("Повторить подключение", async (sender, _) =>
        {
            ((Button)sender).IsEnabled = false;
            try { await client.SignInWindowsAsync(true); await RefreshAsync(); }
            finally { ((Button)sender).IsEnabled = true; }
        }); panel.Children.Add(submit);
        panel.Children.Add(new TextBlock { Text = "Используется учётная запись, под которой запущен помощник. Ввод пароля не требуется. При восстановлении связи вход повторится автоматически.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Foreground = muted });
        root.Children.Add(panel);
    }
    private void MainForm()
    {
        showingLogin = false; root.Children.Clear(); root.ColumnDefinitions.Clear(); root.RowDefinitions.Clear();
        root.ColumnDefinitions.Add(new() { Width = new GridLength(336) }); root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new() { Width = new GridLength(detailsOpen ? 320 : 0) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var toolbar = new WrapPanel();
        toolbar.Children.Add(new TextBlock { Text = client.FullName, FontWeight = FontWeights.Bold, Margin = new Thickness(4, 10, 15, 5) });
        toolbar.Children.Add(Button("Оформление и уведомления", async (_, _) => { var dialog = new MessengerPreferences(settings) { Owner = this }; if (dialog.ShowDialog() == true) { var draft = input?.Text; ApplyTheme(); MainForm(); input!.Text = draft ?? ""; Filter(); RenderHistory(false); await LoadHistoryAsync(); title!.Text = contacts.FirstOrDefault(c => c.Id == peer)?.FullName ?? "Выберите чат"; } }));
        toolbar.Children.Add(Button("Обновить соединение", async (_, _) => await client.SignInWindowsAsync(true)));
        toolbar.Children.Add(Button("Создать группу", async (_, _) => { var window = new ChatGroupWindow(client) { Owner = this }; if (window.ShowDialog() == true) { peer = window.CreatedPeer; messages.Clear(); pendingId = null; await RefreshAsync(); } }));
        toolbar.Children.Add(Button("Участники группы", async (_, _) => { if (peer >= 0) { error!.Text = "Выберите групповой чат."; return; } new ChatGroupWindow(client, peer) { Owner = this }.ShowDialog(); await RefreshAsync(); }));
        var actionsMenu = new ContextMenu { Background = surface, Foreground = ink };
        foreach (var action in toolbar.Children.OfType<Button>().ToArray()) { var item = new MenuItem { Header = action.Content }; item.Click += (_,_) => action.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); actionsMenu.Items.Add(item); }
        if (client.CanBroadcast) { var broadcast = new MenuItem { Header = "Рассылка пользователям…" }; broadcast.Click += (_,_) => new ChatBroadcastWindow(client,settings) { Owner=this }.ShowDialog(); actionsMenu.Items.Add(broadcast); }
        toolbar.Children.Clear(); var menuButton = Button("☰",(_,_) => { actionsMenu.PlacementTarget=toolbar; actionsMenu.IsOpen=true; }); toolbar.Children.Add(menuButton);
        toolbar.Children.Add(new TextBlock { Text="PixelHelper · " + client.FullName, FontWeight=FontWeights.SemiBold, Margin=new Thickness(12,10,12,8) });
        toolbar.Children.Add(Button("ⓘ Информация", async (_,_) => { detailsOpen=!detailsOpen; root.ColumnDefinitions[3].Width=new GridLength(detailsOpen ? 320 : 0); await LoadDetailsAsync(); }));
        toolbar.Children.Add(Button("⌕ Поиск",(_,_)=>SearchHistory()));
        Grid.SetColumnSpan(toolbar, 3); root.Children.Add(toolbar);
        details = new StackPanel { Margin=new Thickness(14,12,10,8) }; var detailsScroll = new ScrollViewer { Content=details, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, Background=surface }; Grid.SetColumn(detailsScroll,2); Grid.SetRow(detailsScroll,1); Grid.SetRowSpan(detailsScroll,3); root.Children.Add(detailsScroll);
        var left = new DockPanel { Margin = new Thickness(0, 8, 12, 8) };
        search = new TextBox { Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8), ToolTip = "Поиск по имени, логину или филиалу" };
        search.TextChanged += (_, _) => Filter(); DockPanel.SetDock(search, Dock.Top); left.Children.Add(search);
        Resources["ContactFont"] = FontSize; Resources["DetailFont"] = Math.Max(11, FontSize - 1); Resources["MutedInk"] = muted;
        users = new ListBox { Background = surface, Foreground = ink, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, ItemTemplate = (DataTemplate)XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Grid Margin="6,4">
                <Grid.RowDefinitions><RowDefinition/><RowDefinition/><RowDefinition/></Grid.RowDefinitions>
                <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                <Border Grid.RowSpan="3" Width="48" Height="48" CornerRadius="24" Background="#6366F1" Margin="0,0,9,0" VerticalAlignment="Center"><TextBlock Text="{Binding Initials}" Foreground="White" HorizontalAlignment="Center" VerticalAlignment="Center" FontSize="13" FontWeight="SemiBold"/></Border>
                <Ellipse Grid.RowSpan="3" Width="9" Height="9" Margin="36,0,9,4" VerticalAlignment="Bottom" Stroke="{DynamicResource MutedInk}" StrokeThickness="1">
                  <Ellipse.Style><Style TargetType="Ellipse"><Setter Property="Fill" Value="#89949E"/><Style.Triggers><DataTrigger Binding="{Binding IsOnline}" Value="True"><Setter Property="Fill" Value="#35C878"/></DataTrigger><DataTrigger Binding="{Binding IsGroup}" Value="True"><Setter Property="Visibility" Value="Collapsed"/></DataTrigger></Style.Triggers></Style></Ellipse.Style>
                </Ellipse>
                <TextBlock Grid.Column="1" Text="{Binding Label}" ToolTip="{Binding FullName}" TextTrimming="CharacterEllipsis" FontSize="{DynamicResource ContactFont}" Margin="0,0,5,4">
                  <TextBlock.Style><Style TargetType="TextBlock"><Style.Triggers><DataTrigger Binding="{Binding HasUnread}" Value="True"><Setter Property="FontWeight" Value="Bold"/></DataTrigger></Style.Triggers></Style></TextBlock.Style>
                </TextBlock>
                <Border Grid.Column="2" Background="#1678AB" CornerRadius="10" Padding="6,1" VerticalAlignment="Top">
                  <Border.Style><Style TargetType="Border"><Setter Property="Visibility" Value="Collapsed"/><Style.Triggers><DataTrigger Binding="{Binding HasUnread}" Value="True"><Setter Property="Visibility" Value="Visible"/></DataTrigger></Style.Triggers></Style></Border.Style>
                  <TextBlock Text="{Binding Unread}" Foreground="White" FontWeight="Bold" FontSize="11"/>
                </Border>
                <TextBlock Grid.Row="1" Grid.Column="1" Grid.ColumnSpan="2" Text="{Binding Subtitle}" TextTrimming="CharacterEllipsis" Foreground="{DynamicResource MutedInk}" FontSize="{DynamicResource DetailFont}"/>
                <TextBlock Grid.Row="2" Grid.Column="1" Grid.ColumnSpan="2" Text="{Binding TimeLabel}" Foreground="{DynamicResource MutedInk}" FontSize="10" Margin="0,4,0,0"/>
              </Grid>
            </DataTemplate>
            """) }; ScrollViewer.SetHorizontalScrollBarVisibility(users, ScrollBarVisibility.Disabled); users.SelectionChanged += async (_, _) =>
        {
            if (updating || users.SelectedItem is not ChatContact contact) return;
            if (peer != contact.Id) { peer = contact.Id; input!.Text = ""; pendingId = null; pendingFiles.Clear(); RenderPendingFiles(); messages.Clear(); }
            await LoadHistoryAsync();
        }; left.Children.Add(users); Grid.SetRow(left, 1); root.Children.Add(left);
        users.PreviewMouseRightButtonDown += (_,e) => { if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(users,source) is ListBoxItem item) users.SelectedItem=item.DataContext; };
        var contactMenu = new ContextMenu { Background=surface, Foreground=ink }; var invite = new MenuItem { Header="Добавить в группу…" }; contactMenu.Items.Add(invite); users.ContextMenu=contactMenu;
        foreach(var (label,kind) in new[]{("Закрепить / открепить","pin"),("Избранное / убрать","favourite"),("Без звука / включить","mute")})
        {var item=new MenuItem { Header=label };item.Click+=async(_,_)=>{if(users.SelectedItem is not ChatContact c)return;try{await client.PreferenceAsync(c.Id,kind=="pin"?!c.Pinned:c.Pinned,kind=="favourite"?!c.Favourite:c.Favourite,kind=="mute"?!c.Muted:c.Muted);await RefreshAsync();}catch(Exception ex){error!.Text=ex.Message;}};contactMenu.Items.Add(item);}
        contactMenu.Opened += async (_,_) =>
        {
            invite.Items.Clear(); invite.IsEnabled=users.SelectedItem is ChatContact { IsGroup:false, IsActive:true };
            if (users.SelectedItem is not ChatContact selected || !invite.IsEnabled) return;
            foreach (var group in contacts.Where(c => c.IsGroup && c.IsActive && c.OwnerId == client.UserId))
            {
                try { var info=await client.GroupAsync(group.Id); if (info.Members.Any(m=>m.Id==selected.Id)) continue; var item=new MenuItem { Header=group.FullName }; item.Click += async (_,_) => { if (MessageBox.Show(this,"Добавить " + selected.FullName + " в «" + group.FullName + "»?",Title,MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; try { await client.GroupOperationAsync(group.Id,"invite",new { userId=selected.Id }); await RefreshAsync(); } catch(Exception ex) { error!.Text=ex.Message; } }; invite.Items.Add(item); } catch(Exception ex) { error!.Text=ex.Message; }
            }
        };
        var right = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
        title = new TextBlock { Text = "Выберите сотрудника", FontSize = FontSize + 4, Margin = new Thickness(4, 0, 4, 8) }; DockPanel.SetDock(title, Dock.Top); right.Children.Add(title);
        var older = Button("Предыдущие сообщения", async (_, _) =>
        {
            if (peer == 0 || messages.Count == 0) return;
            try { var previous = await client.HistoryAsync(peer, messages.Min(m => m.Id)); messages = previous.Concat(messages).DistinctBy(m => m.Id).OrderBy(m => m.Id).ToList(); RenderHistory(false); }
            catch (Exception ex) { error!.Text = ex.Message; }
        }); DockPanel.SetDock(older, Dock.Top); right.Children.Add(older);
        history = new StackPanel(); scroll = new ScrollViewer { Content = history, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = ChatBackdrop(), Padding = new Thickness(8) }; right.Children.Add(scroll);
        Grid.SetColumn(right, 1); Grid.SetRow(right, 1); root.Children.Add(right);
        var composer = new DockPanel(); send = Button("Отправить", async (_, _) => await SendAsync()); DockPanel.SetDock(send, Dock.Right); composer.Children.Add(send);
        var emojiButton = Button("Эмодзи", (_, _) => ShowEmojiPicker()); DockPanel.SetDock(emojiButton, Dock.Left); composer.Children.Add(emojiButton);
        var fileButton = Button("📎", (_, _) => ChooseFiles()); fileButton.ToolTip = "Прикрепить файлы (до 50 МБ каждый)"; DockPanel.SetDock(fileButton, Dock.Left); composer.Children.Add(fileButton);
        input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 4000, MinHeight = 48, MaxHeight = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8) };
        input.KeyDown += async (_, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift) { e.Handled = true; await SendAsync(); } }; composer.Children.Add(input);
        input.PreviewKeyDown += (_, e) => { if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && !sending && input.IsEnabled) { try { if (Clipboard.ContainsImage()) { e.Handled = true; PasteImage(); } else if (Clipboard.ContainsFileDropList()) { e.Handled = true; AddFiles(Clipboard.GetFileDropList().Cast<string>()); } } catch (Exception ex) { error!.Text = "Не удалось вставить вложение: " + ex.Message; } } };
        var composerPanel = new StackPanel(); composerPanel.Children.Add(composer);
        attachmentsPanel = new WrapPanel { MaxHeight = 110 }; composerPanel.Children.Add(new ScrollViewer { Content = attachmentsPanel, MaxHeight = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); RenderPendingFiles();
        var preview = new ContentControl { MaxHeight = 64, Margin = new Thickness(0, 4, 0, 0), ClipToBounds = true, Visibility = Visibility.Collapsed };
        input.ToolTip = "Фирменные эмодзи показываются в предпросмотре и в отправленном сообщении.";
        input.TextChanged+=async(_,_)=>{if(peer!=0&&input.Text.Length>0&&DateTime.UtcNow-typingSent>TimeSpan.FromSeconds(2)){typingSent=DateTime.UtcNow;await client.TypingAsync(peer);}};
        input.TextChanged += (_, _) => { preview.Visibility = HelperEmojis.Parse(input.Text).Any() ? Visibility.Visible : Visibility.Collapsed; if (preview.Visibility == Visibility.Visible) preview.Content = HelperEmojis.Render(input.Text, 28, settings.AnimatedChatEmojis); };
        composerPanel.Children.Add(preview); Grid.SetColumn(composerPanel, 1); Grid.SetRow(composerPanel, 2); root.Children.Add(composerPanel);
        error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) }; Grid.SetRow(error, 3); Grid.SetColumnSpan(error, 2); root.Children.Add(error);
        AddChrome(toolbar);
    }
    private void Filter()
    {
        if (users == null) return;
        bool wasUpdating = updating; updating = true;
        string term = search?.Text.Trim() ?? "";
        users.ItemsSource = ChatContact.Ordered(contacts.Where(u => (chatFilter == "all" || chatFilter == "favourite" && u.Favourite || chatFilter == "unread" && u.Unread > 0 || chatFilter == "groups" && u.IsGroup || chatFilter == "personal" && !u.IsGroup) && (u.FullName + " " + u.Username + " " + u.Branch).Contains(term, StringComparison.OrdinalIgnoreCase))).ToList();
        users.SelectedItem = contacts.FirstOrDefault(u => u.Id == peer); updating = wasUpdating;
    }
    private async Task RefreshAsync()
    {
        if (closing || updating) return;
        if (!client.SignedIn) { if (!showingLogin) LoginForm(); else if (error != null) error.Text = client.SignInStatus; return; }
        if (users == null || showingLogin) MainForm();
        updating = true;
        try { contacts = await client.UsersAsync(); if (peer < 0 && !contacts.Any(c => c.Id == peer)) { peer = 0; messages.Clear(); history?.Children.Clear(); if (title != null) title.Text = "Группа недоступна"; if (input != null) input.Clear(); pendingId = null; } Filter(); if (peer != 0) await LoadHistoryAsync(); }
        catch (Exception ex) { if (error != null) error.Text = ex.Message; }
        finally { updating = false; }
    }
    internal async Task OpenPeerAsync(int id) { peer = id; await RefreshAsync(); await LoadHistoryAsync(); Activate(); }
    private async Task LoadHistoryAsync()
    {
        if (peer == 0 || !client.SignedIn || closing) return;
        int selected = peer;
        var selectedContact=contacts.FirstOrDefault(c=>c.Id==selected);
        if (selectedContact?.SuspendedUntil > DateTime.UtcNow) { messages.Clear(); RenderHistory(false); input!.IsEnabled=false; send!.IsEnabled=false; error!.Text="Доступ к группе отключён до " + selectedContact.SuspendedUntil.Value.ToLocalTime().ToString("dd.MM HH:mm"); return; }
        try
        {
            var latest = await client.HistoryAsync(selected); if (selected != peer || closing) return;
            reactions=await client.ReactionsAsync(selected,latest.Select(m=>m.Id));if(selected!=peer||closing)return;
            messages = messages.Where(m => selected < 0 ? m.RecipientId == selected : m.SenderId == selected || m.RecipientId == selected).Concat(latest).GroupBy(m => m.Id).Select(g => g.Last()).OrderBy(m => m.Id).ToList();
            title!.Text = contacts.FirstOrDefault(u => u.Id == peer)?.FullName ?? "Диалог";
            bool writable = contacts.FirstOrDefault(u => u.Id == peer)?.IsActive == true; input!.IsEnabled = writable && !sending; send!.IsEnabled = writable && !sending;
            RenderHistory(scroll!.ScrollableHeight - scroll.VerticalOffset < 5); await MarkReadAsync(); error!.Text = "";
            await LoadDetailsAsync();
        }
        catch (Exception ex) { if (error != null) error.Text = ex.Message; }
    }
    private void RenderHistory(bool bottom)
    {
        history!.Children.Clear();
        if (messages.Count == 0) history.Children.Add(new TextBlock { Text = "Начните переписку", Foreground = muted, Margin = new Thickness(12) });
        DateTime? date = null;
        foreach (var message in messages.Concat(pendingMessage != null && pendingMessage.RecipientId==peer ? new[]{pendingMessage} : Array.Empty<ChatEntry>()))
        {
            var day = message.SentAt.ToLocalTime().Date;
            if (date != day) { history.Children.Add(new TextBlock { Text=day.ToString("dd MMMM"), HorizontalAlignment=HorizontalAlignment.Center, Foreground=muted, FontSize=Math.Max(11,FontSize-2), Margin=new Thickness(0,10,0,10) }); date=day; }
            bool mine = message.SenderId == client.UserId;
            var panel = new StackPanel();
            if(message.Id==0){panel.Children.Add(new TextBlock {Text=message.Body,TextWrapping=TextWrapping.Wrap});panel.Children.Add(new TextBlock {Text=pendingFailed?"Ошибка · повторите отправку":"Отправляется…",FontSize=11,Foreground=Brushes.White});history.Children.Add(new Border {Child=panel,Background=outgoing,CornerRadius=new CornerRadius(16),Padding=new Thickness(12),Margin=new Thickness(4),MaxWidth=500,HorizontalAlignment=HorizontalAlignment.Right});continue;}
            var reactionMenu=new ContextMenu();foreach(string emoji in new[]{"👍","❤️","😊","🎉","😔","👋"}){var item=new MenuItem{Header=emoji};item.Click+=async(_,_)=>{try{await client.ReactAsync(peer,message.Id,emoji);await LoadHistoryAsync();}catch(Exception ex){error!.Text=ex.Message;}};reactionMenu.Items.Add(item);}panel.ContextMenu=reactionMenu;
            var chips=new WrapPanel();foreach(var reaction in reactions.GetValueOrDefault(message.Id.ToString())??[])chips.Children.Add(Button(reaction.Emoji+" "+reaction.Count,async(_,_)=>{try{await client.ReactAsync(peer,message.Id,reaction.Emoji);await LoadHistoryAsync();}catch(Exception ex){error!.Text=ex.Message;}}));panel.Children.Add(chips);
            if (message.IsUrgent) panel.Children.Add(new TextBlock { Text="⚠ СРОЧНО", Foreground=Brushes.Orange, FontWeight=FontWeights.Bold, Margin=new Thickness(0,0,0,6) });
            if (peer < 0) panel.Children.Add(new TextBlock { Text = message.SenderName ?? "Участник", FontWeight = FontWeights.Bold, Foreground = mine && settings.ChatTheme != "Contrast" ? Brushes.White : accent, Margin = new Thickness(0, 0, 0, 5) });
            var body = HelperEmojis.Render(message.Body, Math.Max(32, FontSize * 2.3), settings.AnimatedChatEmojis); body.FontSize = FontSize; body.Foreground = mine && settings.ChatTheme!="Contrast" ? Brushes.White : ink; panel.Children.Add(body);
            foreach (var attachment in message.Attachments ?? [])
            {
                var file = Button("", async (_, _) => await OpenFileAsync(attachment)); file.Content = new TextBlock { Text = (attachment.IsImage ? "▧ " : "📎 ") + attachment.Label, TextWrapping = TextWrapping.Wrap }; file.HorizontalContentAlignment = HorizontalAlignment.Left; file.ToolTip = attachment.IsImage ? "Просмотр изображения и скачивание" : "Скачать файл"; panel.Children.Add(file);
            }
            if (message.IsUrgent && message.RecipientId == client.UserId && message.AcknowledgedAt == null) panel.Children.Add(Button("Ознакомился",async (_,_) => { try { await client.AcknowledgeAsync(message.Id); await LoadHistoryAsync(); } catch(Exception ex) { error!.Text=ex.Message; } }));
            if (message.AcknowledgedAt != null) panel.Children.Add(new TextBlock { Text="✓ Ознакомление подтверждено", Foreground=accent, FontSize=Math.Max(11,FontSize-2) });
            panel.Children.Add(new TextBlock { Text = message.SentAt.ToLocalTime().ToString("dd.MM HH:mm") + (mine ? peer < 0 || message.ReadAt == null ? " · Сохранено" : " · Прочитано" : ""), FontSize = Math.Max(11, FontSize - 2), Foreground = mine && settings.ChatTheme!="Contrast" ? Brushes.White : muted, Margin = new Thickness(0, 6, 0, 0) });
            history.Children.Add(new Border { Child = panel, Background = mine ? outgoing : incoming, BorderBrush = muted, BorderThickness = new Thickness(settings.ChatTheme == "Contrast" ? 1 : 0), CornerRadius = mine ? new CornerRadius(16,16,5,16) : new CornerRadius(16,16,16,5), Padding = new Thickness(12), Margin = new Thickness(4, 4, 4, 4), MaxWidth = Math.Max(180, (scroll is { ActualWidth: > 0 } ? scroll.ActualWidth : ActualWidth - 320) * .85), HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left });
        }
        if (bottom) scroll!.ScrollToEnd();
    }
    private void AddChrome(WrapPanel toolbar)
    {
        // Shift existing conversation components; the narrow activity bar is a separate fourth zone.
        root.ColumnDefinitions.Insert(0,new ColumnDefinition { Width=new GridLength(68) });
        foreach(UIElement child in root.Children) Grid.SetColumn(child,Grid.GetColumn(child)+1);
        var dock=new DockPanel { Background=surface,Margin=new Thickness(0,0,10,0),LastChildFill=false };
        var top=new StackPanel(); var initials=string.Concat(client.FullName.Split(' ',StringSplitOptions.RemoveEmptyEntries).Take(2).Select(n=>n[0]));
        top.Children.Add(new Border { CornerRadius=new CornerRadius(22),Background=outgoing,Width=44,Height=44,Margin=new Thickness(0,8,0,22),Child=new TextBlock { Text=initials,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center } });
        foreach(var (value,icon,label) in new[] { ("all","☷","Все чаты"),("personal","♙","Личные"),("groups","♟","Рабочие группы"),("unread","◉","Непрочитанные"),("favourite","☆","Избранное") })
        {
            var nav=Button(icon,(_,_)=>{ chatFilter=value; Filter(); });nav.Width=46;nav.Height=46;nav.FontSize=22;nav.BorderThickness=new Thickness(0);nav.ToolTip=label;top.Children.Add(nav);
        }
        DockPanel.SetDock(top,Dock.Top);dock.Children.Add(top);
        var bottom=new StackPanel();bottom.Children.Add(Button("◐",(_,_)=>{settings.ChatTheme=settings.ChatTheme=="Light"?"Dark":"Light";settings.Save();var draft=input?.Text;ApplyTheme();MainForm();input!.Text=draft??"";Filter();RenderHistory(false);title!.Text=contacts.FirstOrDefault(c=>c.Id==peer)?.FullName??"Выберите диалог";}));
        bottom.Children.Add(Button("⚙",(_,_)=>{new MessengerPreferences(settings){Owner=this}.ShowDialog();ApplyTheme();var draft=input?.Text;MainForm();input!.Text=draft??"";Filter();RenderHistory(false);}));DockPanel.SetDock(bottom,Dock.Bottom);dock.Children.Add(bottom);
        Grid.SetRowSpan(dock,4);root.Children.Add(dock);
        toolbar.MouseLeftButtonDown+=(_,e)=>{if(e.ClickCount==2)WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;else if(e.LeftButton==MouseButtonState.Pressed)DragMove();};
        var caption=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right };caption.Children.Add(Button("—",(_,_)=>WindowState=WindowState.Minimized));caption.Children.Add(Button("□",(_,_)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized));caption.Children.Add(Button("×",(_,_)=>Close()));
        root.Children.Remove(toolbar);var header=new DockPanel();DockPanel.SetDock(caption,Dock.Right);header.Children.Add(caption);header.Children.Add(toolbar);Grid.SetColumn(header,1);Grid.SetColumnSpan(header,3);root.Children.Add(header);
    }
    private void SearchHistory()
    {
        if(peer==0)return;int selected=peer;var dialog=new Window {Title="Поиск по переписке",Width=560,Height=540,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};MessengerDialog.Apply(dialog,settings);
        var panel=new DockPanel {Margin=new Thickness(20)};var text=new TextBox {Padding=new Thickness(12),ToolTip="Фраза от двух символов"};DockPanel.SetDock(text,Dock.Top);panel.Children.Add(text);var results=new ListBox {Margin=new Thickness(0,12,0,0)};panel.Children.Add(results);dialog.Content=panel;long revision=0;
        text.TextChanged+=async(_,_)=>{long current=++revision;await Task.Delay(200);if(current!=revision)return;try{results.Items.Clear();if(text.Text.Trim().Length<2)return;var rows=await client.SearchAsync(selected,text.Text);if(current!=revision)return;foreach(var row in rows)results.Items.Add(new TextBlock {Text=row.SentAt.ToLocalTime().ToString("dd.MM HH:mm")+" · "+row.Body,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,4)});}catch(Exception ex){if(current==revision)results.Items.Add(ex.Message);}};dialog.ShowDialog();
    }
    private async Task LoadDetailsAsync()
    {
        if (!detailsOpen || details == null) return; details.Children.Clear(); int selected=peer;
        var contact=contacts.FirstOrDefault(c=>c.Id==selected); if (contact==null) return;
        details.Children.Add(new TextBlock { Text=contact.FullName, FontSize=FontSize+5, FontWeight=FontWeights.SemiBold, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,0,0,12) });
        if (!contact.IsGroup) { details.Children.Add(new TextBlock { Text=contact.IsOnline ? "● В сети" : "○ Не в сети", Foreground=contact.IsOnline ? Brushes.MediumSeaGreen : muted }); details.Children.Add(new TextBlock { Text=contact.Username + "\n" + contact.Branch, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,12,0,0) }); return; }
        try
        {
            var info=await client.GroupAsync(selected); if (selected!=peer || details==null) return;
            details.Children.Add(new TextBlock { Text=info.Members.Count + " участников", Foreground=muted });
            foreach(var member in info.Members)
            {
                var row=Button("",(_,_)=>{}); row.Content=new StackPanel { Children = { new TextBlock { Text=member.Label, TextWrapping=TextWrapping.Wrap }, new TextBlock { Text=member.Id == info.OwnerId ? "Создатель" : member.Detail, FontSize=11, Foreground=muted, TextWrapping=TextWrapping.Wrap } } };
                if (info.OwnerId==client.UserId && !info.IsClosed && member.Id!=client.UserId)
                {
                    var menu=new ContextMenu { Background=surface,Foreground=ink };
                    void Add(string label,string operation,int minutes=0) { var item=new MenuItem { Header=label }; item.Click += async (_,_) => { if (MessageBox.Show(this,label + " — " + member.FullName + "?",Title,MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes) return; try { await client.GroupOperationAsync(selected,operation,new { userId=member.Id,minutes }); await RefreshAsync(); } catch(Exception ex) { error!.Text=ex.Message; } }; menu.Items.Add(item); }
                    Add("Удалить из группы","remove"); Add("Отключить на 1 час","suspend",60); Add("Отключить на 1 день","suspend",1440); Add("Восстановить доступ","resume"); row.ContextMenu=menu;
                }
                details.Children.Add(row);
            }
            details.Children.Add(Button("Управление и приглашение…",async (_,_) => { new ChatGroupWindow(client,selected,settings) { Owner=this }.ShowDialog(); await RefreshAsync(); }));
        }
        catch(Exception ex) { details.Children.Add(new TextBlock { Text=ex.Message,TextWrapping=TextWrapping.Wrap }); }
    }
    private void ShowEmojiPicker()
    {
        if (input?.IsEnabled != true) return;
        var menu = new ContextMenu { Background = surface, Foreground = ink, PlacementTarget = input, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        foreach (var emoji in HelperEmojis.All)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var image = HelperEmojis.AnimatedImage(emoji, 32, settings.AnimatedChatEmojis);
            panel.Children.Add(image); panel.Children.Add(new TextBlock { Text = emoji.Name, VerticalAlignment = VerticalAlignment.Center });
            var item = new MenuItem { Header = panel }; item.Click += (_, _) => InsertEmoji(emoji); menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
    internal void InsertEmoji(HelperEmoji emoji)
    {
        if (input == null || !input.IsEnabled) return;
        if (input.Text.Length - input.SelectionLength + emoji.Code.Length > 4000) { error!.Text = "Сообщение не должно превышать 4000 символов."; return; }
        int index = input.SelectionStart; input.SelectedText = emoji.Code; input.CaretIndex = index + emoji.Code.Length; input.Focus();
        EmojiInserted?.Invoke(emoji);
    }
    private async Task MarkReadAsync()
    {
        if (!IsActive || peer == 0 || messages.Count == 0 || !client.SignedIn || scroll?.ScrollableHeight-scroll?.VerticalOffset>100) return;
        var unread = messages.Where(m => (peer < 0 ? m.SenderId != client.UserId : m.RecipientId == client.UserId) && m.ReadAt == null).ToList();
        if (unread.Count == 0) return;
        int selected = peer; long through = unread.Max(m => m.Id);
        try
        {
            await client.ReadAsync(selected, through);
            contacts = contacts.Select(c => c.Id == selected && (c.LastId ?? 0) <= through ? c with { Unread = 0 } : c).ToList(); Filter();
            if (peer == selected) messages = messages.Select(m => unread.Any(u => u.Id == m.Id) ? m with { ReadAt = DateTime.UtcNow } : m).ToList();
        }
        catch (Exception ex) { if (error != null) error.Text = ex.Message; }
    }
    private async Task SendAsync()
    {
        if (sending || send?.IsEnabled != true || peer == 0 || string.IsNullOrWhiteSpace(input?.Text) && pendingFiles.Count == 0) return;
        string text = input!.Text.Trim();
        string signature;
        try { signature = text + string.Join("|", pendingFiles.Select(path => { var file = new FileInfo(path); return path + ":" + file.Length + ":" + file.LastWriteTimeUtc.Ticks; })); }
        catch (IOException ex) { error!.Text = ex.Message; return; }
        if (pendingId == null || pendingText != signature || pendingPeer != peer) { pendingId = Guid.NewGuid().ToString("N"); pendingText = signature; pendingPeer = peer; }
        sending = true; send.IsEnabled = false; input.IsEnabled = false; users!.IsEnabled = false; error!.Text = "Отправляется…";
        pendingMessage=new ChatEntry(0,client.UserId,peer,text.Length>0?text:"Вложения: "+string.Join(", ",pendingFiles.Select(Path.GetFileName)),pendingId,DateTime.UtcNow,null);pendingFailed=false;RenderHistory(true);
        int target = peer;
        try { if (pendingFiles.Count == 0) await client.SendAsync(target, text, pendingId); else await client.SendFilesAsync(target, text, pendingId, pendingFiles.ToArray()); pendingMessage=null;if (peer == target && input.Text.Trim() == text) { input.Clear(); pendingFiles.Clear(); RenderPendingFiles(); pendingId = null; } await LoadHistoryAsync(); }
        catch (Exception ex) { pendingFailed=true;error.Text = ex.Message;RenderHistory(true); }
        finally { sending = false; users!.IsEnabled = true; input.IsEnabled = send.IsEnabled = contacts.FirstOrDefault(c => c.Id == peer)?.IsActive == true; }
    }
    private void ChooseFiles()
    {
        if (sending || peer == 0 || input?.IsEnabled != true) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "Все файлы|*.*" };
        if (dialog.ShowDialog(this) == true) AddFiles(dialog.FileNames);
    }
    internal void AddFiles(IEnumerable<string> paths)
    {
        if (sending || input?.IsEnabled != true || peer == 0) return;
        try
        {
            var proposed = pendingFiles.Concat(paths).Distinct().ToList();
            if (proposed.Count > 10 || proposed.Any(p => !File.Exists(p) || new FileInfo(p).Length > 50 * 1024 * 1024) || proposed.Sum(p => new FileInfo(p).Length) > 100 * 1024 * 1024) throw new IOException("Не более 10 файлов: до 50 МБ каждый и до 100 МБ всего.");
            pendingFiles.Clear(); pendingFiles.AddRange(proposed); pendingId = null; RenderPendingFiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error!.Text = ex.Message; }
    }
    private void PasteImage()
    {
        var bitmap = Clipboard.GetImage(); if (bitmap == null || peer == 0) return;
        AttachImage(bitmap);
    }
    internal void AttachImage(BitmapSource bitmap)
    {
        if (bitmap.PixelWidth * (long)bitmap.PixelHeight > 40_000_000) { error!.Text = "Изображение слишком большое. Прикрепите его как файл."; return; }
        Directory.CreateDirectory(clipboardDirectory); string path = Path.Combine(clipboardDirectory, "Изображение-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + ".png");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(path)) encoder.Save(file); AddFiles([path]);
    }
    private void RenderPendingFiles()
    {
        if (attachmentsPanel == null) return; attachmentsPanel.Children.Clear();
        foreach (string path in pendingFiles.ToArray()) { var button = Button(Path.GetFileName(path) + " ×", (_, _) => { if (sending) return; pendingFiles.Remove(path); pendingId = null; RenderPendingFiles(); }); button.ToolTip = "Убрать вложение"; attachmentsPanel.Children.Add(button); }
    }
    private async Task SaveFileAsync(ChatAttachment file)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = file.Name, Filter = "Все файлы|*.*" };
        if (dialog.ShowDialog(this) != true) return; await client.DownloadAsync(file, dialog.FileName);
    }
    private async Task OpenFileAsync(ChatAttachment file)
    {
        try
        {
            if (!file.IsImage) { await SaveFileAsync(file); return; }
            Directory.CreateDirectory(clipboardDirectory); string path = Path.Combine(clipboardDirectory, Guid.NewGuid().ToString("N"));
            try
            {
                await client.DownloadAsync(file, path);
                var panel = new DockPanel { Margin = new Thickness(12) };
                var download = Button("Скачать изображение", async (_, _) => { try { await SaveFileAsync(file); } catch (Exception ex) { MessageBox.Show(ex.Message, "Скачивание"); } }); DockPanel.SetDock(download, Dock.Bottom); panel.Children.Add(download);
                try { BitmapSource image; using (var stream = File.OpenRead(path)) { var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None); var frame = decoder.Frames[0]; if ((long)frame.PixelWidth * frame.PixelHeight > 100_000_000 || frame.PixelWidth == 0 || frame.PixelHeight == 0) throw new NotSupportedException(); stream.Position = 0; var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; double scale = Math.Min(1, Math.Min(1600d / frame.PixelWidth, 1200d / frame.PixelHeight)); bitmap.DecodePixelWidth = Math.Max(1, (int)(frame.PixelWidth * scale)); bitmap.EndInit(); bitmap.Freeze(); image = bitmap; } panel.Children.Add(new ScrollViewer { Content = new Image { Source = image, Stretch = Stretch.Uniform }, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); }
                catch (Exception ex) when (ex is NotSupportedException or IOException or ArgumentException or FormatException) { panel.Children.Add(new TextBlock { Text = "Предпросмотр этого изображения недоступен. Вы можете скачать оригинал.", TextWrapping = TextWrapping.Wrap }); }
                new Window { Title = file.Name, Content = panel, Owner = this, Width = 800, Height = 600, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = surface, Foreground = ink }.ShowDialog();
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        catch (Exception ex) { error!.Text = "Не удалось открыть вложение: " + ex.Message; }
    }
}
