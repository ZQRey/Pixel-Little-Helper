using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelHelper;
internal sealed class ChatGroupWindow : Window
{
    private readonly MessengerClient client;
    private readonly int peer;
    private readonly StackPanel panel = new() { Margin = new Thickness(18) };
    private readonly TextBlock status = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private bool busy;
    internal int CreatedPeer { get; private set; }
    internal ChatGroupWindow(MessengerClient client, int peer = 0)
    {
        this.client = client; this.peer = peer;
        Title = peer == 0 ? "Создать группу" : "Участники и управление группой"; Width = 520; Height = 620; MinWidth = 450; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Content = new ScrollViewer { Content = panel }; Background = Brushes.WhiteSmoke;
        Loaded += async (_, _) => await LoadAsync();
    }
    private Button Button(string label, Func<Task> action)
    {
        var button = new Button { Content = label, Style = PetWindow.AssistantButtonStyle, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 5, 0, 0) };
        button.Click += async (_, _) =>
        {
            if (busy) return; busy = true; button.IsEnabled = false; status.Text = "";
            try { await action(); } catch (Exception ex) { status.Text = ex.Message; }
            finally { busy = false; button.IsEnabled = true; }
        }; return button;
    }
    private async Task LoadAsync()
    {
        try
        {
            panel.Children.Clear();
            var contacts = (await client.UsersAsync()).Where(c => !c.IsGroup && c.IsActive).OrderBy(c => c.FullName).ToList();
            if (peer == 0)
            {
                panel.Children.Add(new TextBlock { Text = "Название группы", FontSize = 18 });
                var name = new TextBox { MaxLength = 80, Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 12) }; panel.Children.Add(name);
                panel.Children.Add(new TextBlock { Text = "Выберите участников (Ctrl / Shift для нескольких)" });
                var users = new ListBox { ItemsSource = contacts, DisplayMemberPath = "DirectoryLabel", SelectionMode = SelectionMode.Multiple, Height = 300, Margin = new Thickness(0, 8, 0, 8) }; panel.Children.Add(users);
                panel.Children.Add(Button("Создать", async () => { var result = await client.CreateGroupAsync(name.Text, users.SelectedItems.Cast<ChatContact>().Select(u => u.Id).ToArray()); CreatedPeer = result.GetProperty("id").GetInt32(); DialogResult = true; }));
            }
            else
            {
                var info = await client.GroupAsync(peer); bool owner = info.OwnerId == client.UserId;
                panel.Children.Add(new TextBlock { Text = info.Name + (info.IsClosed ? " · Закрыта" : ""), FontSize = 20, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(new TextBlock { Text = "Участники: " + info.Members.Count, Margin = new Thickness(0, 10, 0, 6) });
                var members = new ListBox { Height = 200, ItemsSource = info.Members.Select(m => new MemberItem(m.Id, m.FullName + " (" + m.Username + ")" + (m.Id == info.OwnerId ? " · Создатель" : "") + (!m.IsActive ? " · Отключён" : ""))).ToList(), DisplayMemberPath = "Name" }; panel.Children.Add(members);
                int Selected() => members.SelectedItem is MemberItem m ? m.Id : throw new InvalidOperationException("Выберите участника.");
                if (owner && !info.IsClosed)
                {
                    var name = new TextBox { Text = info.Name, MaxLength = 80, Padding = new Thickness(6), Margin = new Thickness(0, 10, 0, 0) }; panel.Children.Add(name);
                    panel.Children.Add(Button("Сохранить название", async () => { await client.GroupOperationAsync(peer, "rename", new { name = name.Text }); await LoadAsync(); }));
                    var available = new ComboBox { ItemsSource = contacts.Where(c => info.Members.All(m => m.Id != c.Id)).ToList(), DisplayMemberPath = "DirectoryLabel", Margin = new Thickness(0, 10, 0, 0) }; panel.Children.Add(available);
                    panel.Children.Add(Button("Пригласить выбранного сотрудника", async () => { if (available.SelectedItem is not ChatContact user) throw new InvalidOperationException("Выберите сотрудника."); await client.GroupOperationAsync(peer, "invite", new { userId = user.Id }); await LoadAsync(); }));
                    panel.Children.Add(Button("Удалить выбранного участника", async () => { int id = Selected(); if (MessageBox.Show(this, "Удалить участника? Он потеряет доступ к группе.", Title, MessageBoxButton.YesNo) == MessageBoxResult.Yes) { await client.GroupOperationAsync(peer, "remove", new { userId = id }); await LoadAsync(); } }));
                    panel.Children.Add(Button("Передать владение выбранному участнику", async () => { int id = Selected(); if (MessageBox.Show(this, "Передать управление группой этому участнику?", Title, MessageBoxButton.YesNo) == MessageBoxResult.Yes) { await client.GroupOperationAsync(peer, "owner", new { userId = id }); await LoadAsync(); } }));
                    panel.Children.Add(Button("Закрыть группу", async () => { if (MessageBox.Show(this, "Закрыть группу? История останется, отправка сообщений прекратится.", Title, MessageBoxButton.YesNo) == MessageBoxResult.Yes) { await client.GroupOperationAsync(peer, "close"); await LoadAsync(); } }));
                }
                panel.Children.Add(Button("Выйти из группы", async () => { if (MessageBox.Show(this, "Выйти из группы? Доступ к её истории прекратится.", Title, MessageBoxButton.YesNo) == MessageBoxResult.Yes) { await client.GroupOperationAsync(peer, "leave"); DialogResult = true; } }));
            }
            panel.Children.Add(status);
        }
        catch (Exception ex) { if (!panel.Children.Contains(status)) panel.Children.Add(status); status.Text = ex.Message; }
    }
    private record MemberItem(int Id, string Name);
}
