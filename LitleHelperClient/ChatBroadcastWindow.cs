using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PixelHelper;
internal sealed class ChatBroadcastWindow : Window
{
    internal ChatBroadcastWindow(MessengerClient client, Settings settings)
    {
        Title = "Рассылка в мессенджере"; Width = 640; Height = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner; MessengerDialog.Apply(this, settings);
        var panel = new StackPanel { Margin = new Thickness(20) }; Content = new ScrollViewer { Content = panel };
        panel.Children.Add(new TextBlock { Text = "Новое объявление", FontSize = FontSize + 7 });
        var audience = new ComboBox { ItemsSource = new[] { "Все включённые пользователи", "Только онлайн сейчас" }, SelectedIndex = 0, Margin = new Thickness(0,15,0,8) }; panel.Children.Add(audience);
        var urgent = new CheckBox { Content = "Срочное · с подтверждением ознакомления", Foreground = Foreground, Margin = new Thickness(0,5,0,12) }; panel.Children.Add(urgent);
        var text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 160, MaxLength = 4000 }; panel.Children.Add(text);
        var paths = new List<string>(); var files = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,8) }; panel.Children.Add(files);
        var attach = new Button { Content = "Прикрепить файлы…" }; panel.Children.Add(attach);
        attach.Click += (_,_) => { var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "Все файлы|*.*" }; if (dialog.ShowDialog(this) == true) { var selected = dialog.FileNames; if (selected.Length > 10 || selected.Any(p => new FileInfo(p).Length > 50 * 1024 * 1024) || selected.Sum(p => new FileInfo(p).Length) > 100 * 1024 * 1024) { MessageBox.Show(this,"До 10 файлов, до 50 МБ каждый и 100 МБ всего."); return; } paths.Clear(); paths.AddRange(selected); files.Text = string.Join("\n",paths.Select(Path.GetFileName)); } };
        var clear = new Button { Content = "Убрать вложения", Margin = new Thickness(0,5,0,8) }; clear.Click += (_,_) => { paths.Clear(); files.Text=""; }; panel.Children.Add(clear);
        var status = new TextBlock { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,8,0,8) }; panel.Children.Add(status);
        var send = new Button { Content = "Просмотреть и отправить", Margin = new Thickness(0,8,0,12) }; panel.Children.Add(send);
        string? pendingId = null, signature = null;
        send.Click += async (_,_) =>
        {
            if (string.IsNullOrWhiteSpace(text.Text) && paths.Count == 0) { status.Text="Введите сообщение или прикрепите файл."; return; }
            panel.IsEnabled=false;
            try
            {
                var users = (await client.UsersAsync()).Where(u => !u.IsGroup && u.IsActive && (audience.SelectedIndex == 0 || u.IsOnline)).ToList();
                if (users.Count == 0) { status.Text="Нет получателей."; return; }
                string preview = (urgent.IsChecked == true ? "СРОЧНО" : "Обычное сообщение") + " · Получателей сейчас: " + users.Count + "\n\n" + text.Text + "\n\n" + files.Text + "\n\nОтправить рассылку?";
                if (MessageBox.Show(this,preview,Title,MessageBoxButton.YesNo,MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                string next = text.Text + audience.SelectedIndex + urgent.IsChecked + string.Join("|",paths.Select(p => p+new FileInfo(p).Length+new FileInfo(p).LastWriteTimeUtc.Ticks)); if (next != signature) { signature=next; pendingId=Guid.NewGuid().ToString("N"); }
                var result = await client.BroadcastAsync(text.Text,audience.SelectedIndex == 0 ? "all" : "online",urgent.IsChecked == true,pendingId!,paths);
                status.Text="Отправлено получателям: " + result.GetProperty("recipients").GetInt32(); pendingId=null; signature=null; text.Clear(); paths.Clear(); files.Text="";
            }
            catch (Exception ex) { status.Text=ex.Message; }
            finally { panel.IsEnabled=true; }
        };
        var reports = new StackPanel(); var refresh = new Button { Content = "Мои рассылки · обновить отчёт" }; panel.Children.Add(refresh); panel.Children.Add(reports);
        refresh.Click += async (_,_) => { try { reports.Children.Clear(); var data=await client.BroadcastsAsync(); foreach(var row in data.EnumerateArray()) reports.Children.Add(new TextBlock { Text = row.GetProperty("body").GetString() + "\nПолучателей: " + row.GetProperty("recipients") + " · Прочитали: " + row.GetProperty("read") + " · Подтвердили: " + row.GetProperty("acknowledged"), TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,10,0,10) }); } catch(Exception ex) { status.Text=ex.Message; } };
    }
}
