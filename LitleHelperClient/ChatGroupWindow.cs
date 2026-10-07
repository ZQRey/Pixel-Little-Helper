using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
namespace PixelHelper;
internal static class MessengerDialog
{
    internal static void Apply(Window window, Settings settings)
    {
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("/PixelHelper;component/MessengerControls.xaml",UriKind.Relative) }); bool light = settings.ChatTheme == "Light"; var surface = (Brush)new BrushConverter().ConvertFromString(light ? "#FFFFFF" : settings.ChatTheme == "Contrast" ? "#000000" : "#181F2F")!;
        window.Background = (Brush)new BrushConverter().ConvertFromString(light ? "#F0F2F7" : "#0A0D14")!; window.Foreground = light ? Brushes.DarkSlateGray : Brushes.WhiteSmoke; window.FontFamily = new FontFamily("Segoe UI"); window.FontSize = Math.Clamp(settings.ChatFontSize, 11, 22);
        foreach (var type in new[] { typeof(TextBox), typeof(ListBox), typeof(ComboBox), typeof(ComboBoxItem), typeof(CheckBox), typeof(Button) })
        { var style = new Style(type, type==typeof(Button) || type==typeof(ComboBox) || type==typeof(ComboBoxItem) || type==typeof(CheckBox) ? (Style)window.FindResource(type) : null); style.Setters.Add(new Setter(Control.BackgroundProperty, surface)); style.Setters.Add(new Setter(Control.ForegroundProperty, window.Foreground)); style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.SlateBlue)); style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8))); window.Resources[type] = style; }
    }
    internal static bool Matches(ChatContact user, string text) => (user.FullName + " " + user.Username + " " + user.Branch).Contains(text.Trim(), StringComparison.OrdinalIgnoreCase);
}
internal sealed class ChatGroupWindow : Window
{
    private readonly MessengerClient client; private readonly int peer;
    private readonly StackPanel panel = new() { Margin = new Thickness(18) };
    private readonly TextBlock status = new() { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,0) };
    private bool busy; internal int CreatedPeer { get; private set; }
    internal ChatGroupWindow(MessengerClient client, int peer = 0, Settings? settings = null)
    {
        this.client = client; this.peer = peer; Title = peer == 0 ? "Создать группу" : "Участники группы"; Width = 540; Height = 670; MinWidth = 440; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Content = new ScrollViewer { Content = panel }; MessengerDialog.Apply(this, settings ?? Settings.Load()); Loaded += async (_, _) => await LoadAsync();
    }
    private Button Button(string title, Func<Task> action)
    {
        var button = new Button { Content = title, Margin = new Thickness(0,8,0,0) }; button.Click += async (_, _) => await Run(action); return button;
    }
    private async Task Run(Func<Task> action) { if (busy) return; busy = true; panel.IsEnabled = false; status.Text = ""; try { await action(); } catch (Exception ex) { status.Text = ex.Message; } finally { busy = false; panel.IsEnabled = true; } }
    private ListBox Directory(List<ChatContact> contacts, bool multiple)
    {
        var search = new TextBox { ToolTip = "Поиск по части имени, логина или филиала", Margin = new Thickness(0,8,0,6) }; panel.Children.Add(search);
        var list = new ListBox { ItemsSource = contacts, DisplayMemberPath = "DirectoryLabel", Height = multiple ? 270 : 140, SelectionMode = multiple ? SelectionMode.Multiple : SelectionMode.Single }; panel.Children.Add(list);
        var selected = new HashSet<int>(); bool filtering = false;
        list.SelectionChanged += (_, e) => { if (filtering) return; foreach (ChatContact c in e.AddedItems) selected.Add(c.Id); foreach (ChatContact c in e.RemovedItems) selected.Remove(c.Id); };
        search.TextChanged += (_, _) => { filtering = true; list.ItemsSource = contacts.Where(c => MessengerDialog.Matches(c, search.Text)).ToList(); foreach (ChatContact c in list.Items) if (selected.Contains(c.Id)) { if (multiple) list.SelectedItems.Add(c); else list.SelectedItem=c; } filtering = false; }; list.Tag = selected; return list;
    }
    private async Task LoadAsync()
    {
        try
        {
            panel.Children.Clear(); var contacts = (await client.UsersAsync()).Where(c => !c.IsGroup && c.IsActive).OrderBy(c => c.FullName).ToList();
            if (peer == 0)
            {
                panel.Children.Add(new TextBlock { Text = "Новая группа", FontSize = FontSize + 6 }); var name = new TextBox { MaxLength = 80, Margin = new Thickness(0,12,0,6), ToolTip = "Название группы" }; panel.Children.Add(name);
                panel.Children.Add(new TextBlock { Text = "Участники · Ctrl для нескольких" }); var users = Directory(contacts, true);
                panel.Children.Add(Button("Создать группу", async () => { var ids = ((HashSet<int>)users.Tag).ToArray(); var result = await client.CreateGroupAsync(name.Text, ids); CreatedPeer = result.GetProperty("id").GetInt32(); DialogResult = true; }));
            }
            else
            {
                var info = await client.GroupAsync(peer); bool owner = info.OwnerId == client.UserId && !info.IsClosed;
                panel.Children.Add(new TextBlock { Text = info.Name, FontSize = FontSize + 6, TextWrapping = TextWrapping.Wrap }); panel.Children.Add(new TextBlock { Text = info.Members.Count + " участников" + (info.IsClosed ? " · Закрыта" : ""), Margin = new Thickness(0,5,0,12) });
                var members = new ListBox { ItemsSource = info.Members, Height = 230, ItemTemplate = (DataTemplate)XamlReader.Parse("""
                    <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><StackPanel Margin="4,6"><TextBlock Text="{Binding Label}" FontWeight="SemiBold"/><TextBlock Text="{Binding Detail}" Opacity="0.7" FontSize="11"/></StackPanel></DataTemplate>
                    """) }; panel.Children.Add(members);
                members.PreviewMouseRightButtonDown += (_, e) => { if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(members,source) is ListBoxItem item) members.SelectedItem=item.DataContext; };
                var menu = new ContextMenu();
                void Action(string label, Func<ChatMember,Task> action) { var item = new MenuItem { Header = label }; item.Click += async (_, _) => { if (members.SelectedItem is ChatMember m) await Run(() => action(m)); }; menu.Items.Add(item); }
                if (owner)
                {
                    Action("Удалить из группы", async m => { if (Confirm("Удалить " + m.FullName + " из группы?")) { await client.GroupOperationAsync(peer,"remove",new { userId = m.Id }); await LoadAsync(); } });
                    Action("Временно отключить…", async m => { int? minutes = Interval(); if (minutes != null && Confirm("Отключить " + m.FullName + " на " + minutes + " минут?")) { await client.GroupOperationAsync(peer,"suspend",new { userId=m.Id, minutes }); await LoadAsync(); } });
                    Action("Восстановить доступ", async m => { if (Confirm("Восстановить доступ " + m.FullName + "?")) { await client.GroupOperationAsync(peer,"resume",new { userId=m.Id, minutes=0 }); await LoadAsync(); } });
                    Action("Передать владение", async m => { if (Confirm("Передать группу " + m.FullName + "?")) { await client.GroupOperationAsync(peer,"owner",new { userId=m.Id }); await LoadAsync(); } });
                    menu.Opened += (_, _) => { foreach (MenuItem item in menu.Items) item.IsEnabled = members.SelectedItem is ChatMember m && m.Id != info.OwnerId; }; members.ContextMenu = menu;
                    panel.Children.Add(new TextBlock { Text = "Правый клик по участнику — управление", Opacity = .7, Margin = new Thickness(0,6,0,10) });
                    var rename = new TextBox { Text = info.Name, MaxLength = 80 }; panel.Children.Add(rename); panel.Children.Add(Button("Сохранить название", async () => { await client.GroupOperationAsync(peer,"rename",new { name=rename.Text }); await LoadAsync(); }));
                    panel.Children.Add(new TextBlock { Text = "Пригласить сотрудника", Margin = new Thickness(0,15,0,0) }); var available = Directory(contacts.Where(c => info.Members.All(m => m.Id != c.Id)).ToList(), false);
                    panel.Children.Add(Button("Пригласить выбранного", async () => { if (available.SelectedItem is not ChatContact u) throw new InvalidOperationException("Выберите сотрудника."); await client.GroupOperationAsync(peer,"invite",new { userId=u.Id }); await LoadAsync(); }));
                    panel.Children.Add(Button("Закрыть группу", async () => { if (Confirm("Закрыть группу? История останется доступна.")) { await client.GroupOperationAsync(peer,"close"); await LoadAsync(); } }));
                }
                panel.Children.Add(Button("Выйти из группы", async () => { if (Confirm("Выйти из группы?")) { await client.GroupOperationAsync(peer,"leave"); DialogResult = true; } }));
            }
            panel.Children.Add(status);
        }
        catch (Exception ex) { if (!panel.Children.Contains(status)) panel.Children.Add(status); status.Text = ex.Message; }
    }
    private bool Confirm(string text) => MessageBox.Show(this,text,Title,MessageBoxButton.YesNo,MessageBoxImage.Question) == MessageBoxResult.Yes;
    private int? Interval()
    {
        var panel = new StackPanel { Margin = new Thickness(18) }; panel.Children.Add(new TextBlock { Text = "Отключить на сколько минут? (1–43200)" }); var value = new TextBox { Text = "60", Margin = new Thickness(0,10,0,10) }; panel.Children.Add(value);
        var dialog = new Window { Title="Временное отключение", Width=360, Height=190, Owner=this, Content=panel, WindowStartupLocation=WindowStartupLocation.CenterOwner }; MessengerDialog.Apply(dialog, Settings.Load()); var apply = new Button { Content="Продолжить", IsDefault=true }; panel.Children.Add(apply); int? result=null; apply.Click += (_,_) => { if (int.TryParse(value.Text,out int minutes) && minutes is >=1 and <=43200) { result=minutes; dialog.DialogResult=true; } }; dialog.ShowDialog(); return result;
    }
}
