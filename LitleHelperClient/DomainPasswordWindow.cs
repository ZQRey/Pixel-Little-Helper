using System.Windows;
using System.Windows.Controls;
namespace PixelHelper;
public sealed class DomainPasswordWindow : Window
{
    private readonly PasswordBox current = new(), next = new(), confirmation = new();
    public string CurrentPassword => current.Password;
    public string NewPassword => next.Password;
    private static string Text(string ru,string kk,string en,string zh)=>Loc.Code switch {"kk"=>kk,"en"=>en,"zh"=>zh,_=>ru};
    public DomainPasswordWindow()
    {
        Title = Text("Смена пароля домена","Домен құпиясөзін өзгерту","Change domain password","修改域密码"); Width = 370; Height = 300; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20) }; Content = panel;
        foreach (var pair in new[] {(Text("Текущий пароль","Ағымдағы құпиясөз","Current password","当前密码"),current),(Text("Новый пароль","Жаңа құпиясөз","New password","新密码"),next),(Text("Повторите новый пароль","Жаңа құпиясөзді қайталаңыз","Confirm new password","确认新密码"),confirmation) })
        { panel.Children.Add(new TextBlock { Text=pair.Item1, Margin=new Thickness(0,8,0,4) }); panel.Children.Add(pair.Item2); }
        var submit = new Button {Content=Text("Изменить пароль","Құпиясөзді өзгерту","Change password","修改密码"), Margin=new Thickness(0,16,0,0), IsDefault=true};
        submit.Click += (_,_) => {
            if (string.IsNullOrEmpty(current.Password) || string.IsNullOrEmpty(next.Password) || next.Password != confirmation.Password)
            { MessageBox.Show(this,Text("Заполните текущий пароль и два одинаковых новых пароля.","Ағымдағы құпиясөзді және бірдей екі жаңа құпиясөзді енгізіңіз.","Enter the current password and two matching new passwords.","输入当前密码和两次一致的新密码。")); return; }
            DialogResult=true;
        }; panel.Children.Add(submit);
    }
}
