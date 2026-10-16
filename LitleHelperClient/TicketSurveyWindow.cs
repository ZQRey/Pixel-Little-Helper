using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelHelper;

internal sealed class TicketSurveyWindow : Window
{
    private readonly HubConnectionService hub;
    private readonly TicketSurveyNotice notice;
    private string requestId = Guid.NewGuid().ToString();
    private readonly TextBlock error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox comment = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 75, MaxLength = 2000, Margin = new(0,8,0,12) };
    private readonly List<Button> stars = [];
    private readonly StackPanel root = new() { Margin = new(24) };
    private int score;
    private bool completed, busy, closing;
    internal string SurveyId => notice.Id;
    internal bool UrgentRequested { get; private set; }
    internal bool Submitted { get; private set; }
    internal bool Busy => busy;
    internal void CloseFromServer() { completed = true; Close(); }
    public TicketSurveyWindow(HubConnectionService hub, TicketSurveyNotice notice, bool allowUrgent = false)
    {
        this.hub = hub; this.notice = notice;
        Title = "Оценка результата заявки"; Width = 460; Height = 610; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = Brushes.White;
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(new TextBlock { Text = "Заявка закрыта · #"+notice.TicketId, FontSize = 19, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock { Text = notice.Title, TextWrapping = TextWrapping.Wrap, Margin = new(0,10,0,6) });
        root.Children.Add(new TextBlock { Text = notice.Specialist, Foreground = Brushes.SlateGray });
        root.Children.Add(new TextBlock { Text = (notice.Mandatory ? "Оценка обязательна. " : "")+"Ответить можно до "+notice.ExpiresAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"), TextWrapping = TextWrapping.Wrap, Margin = new(0,8,0,0) });
        if (notice.State != "Pending") {
            root.Children.Add(new TextBlock { Text = notice.State == "Writing" ? "Оценка принята помощником и ожидает записи в GLPI. Повторно отвечать не нужно." : notice.Error, TextWrapping = TextWrapping.Wrap, Margin = new(0,15,0,0) });
            if (notice.State == "Writing") { completed = true; return; }
        }
        var row = new WrapPanel { Margin = new(0,15,0,12) }; root.Children.Add(row);
        for (int n=1;n<=5;n++) {
            int selected=n; var b = new Button { Content="☆",FontSize=28,Width=62,Height=48,Margin=new(0,0,8,0),Background=Brushes.White };
            System.Windows.Automation.AutomationProperties.SetName(b,n+" из 5 звёзд");
            b.Click += (_,_) => { score=selected; for(int i=0;i<stars.Count;i++) { stars[i].Content=i<score?"★":"☆"; stars[i].Foreground=i<score?Brushes.DarkGoldenrod:Brushes.SlateGray; } };
            stars.Add(b);row.Children.Add(b);
        }
        root.Children.Add(new TextBlock { Text="Комментарий (необязательно)" });root.Children.Add(comment);root.Children.Add(error);
        var actions = new WrapPanel { Margin=new(0,10,0,0) };root.Children.Add(actions);
        Button Action(string title,Func<System.Threading.Tasks.Task> run) {
            var b=new Button { Content=title,Padding=new(12,8,12,8),Margin=new(0,0,8,8) };
            b.Click += async (_,_) => { if(busy)return;busy=true;root.IsEnabled=false;try { await run(); } catch(Exception e) { error.Text=e.Message; } finally { busy=false;root.IsEnabled=true; } };
            actions.Children.Add(b);return b;
        }
        Action("Отправить оценку",async () => {
            if(score==0) { error.Text="Выберите от 1 до 5 звёзд";return; }
            var result=await hub.AnswerSurvey(notice.Id,score,comment.Text,requestId);
            if(!result.Saved && !result.Queued) { requestId=Guid.NewGuid().ToString();error.Text=result.Error??"GLPI не подтвердил запись";return; }
            Submitted=true;completed=true;Close();
            if(result.Queued) MessageBox.Show("Оценка принята помощником. При восстановлении связи она будет записана в GLPI. Срок оценки — 14 дней после закрытия заявки.","Оценка ожидает записи",MessageBoxButton.OK,MessageBoxImage.Information);
        });
        if(!notice.Mandatory || notice.DeferralsLeft>0 && notice.DueAt>DateTime.UtcNow) {
            foreach(var minutes in new[]{30,120}) Action(minutes==30?"На 30 минут":"На 2 часа",async () => { await hub.SnoozeSurvey(notice.Id,minutes);completed=true;Close(); });
        }
        if(!notice.Mandatory) Action("Не напоминать",async () => { await hub.DismissSurvey(notice.Id);completed=true;Close(); });
        if(allowUrgent) Action("Срочная новая проблема",() => { UrgentRequested=true;completed=true;Close();return System.Threading.Tasks.Task.CompletedTask; });
        Closing += OnClosing;
    }
    private async void OnClosing(object? sender,CancelEventArgs e)
    {
        if(completed)return;
        e.Cancel=true;if(busy||closing)return;closing=true;
        try { await hub.SnoozeSurvey(notice.Id,30);completed=true;Close(); }
        catch(Exception ex) { Settings.Log(ex);completed=true;Close(); }
        finally { closing=false; }
    }
}
