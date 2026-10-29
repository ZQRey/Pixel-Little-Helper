using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelHelper;

public sealed class NonAdEnrollmentWindow : Window
{
    private readonly Settings settings;
    private readonly TextBox fullNameBox;
    private readonly TextBox inventoryBox;
    private readonly TextBox branchBox;
    private readonly TextBlock errorText;
    private bool registered;

    public NonAdEnrollmentWindow(Settings settings)
    {
        this.settings = settings;

        Title = "Регистрация устройства вне Active Directory";
        Width = 480;
        Height = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = true;
        Background = Brushes.Transparent;
        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;

        var rootBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235)), // Blue 600
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), // Slate 900
            Padding = new Thickness(24)
        };

        var stack = new StackPanel();

        var titleBlock = new TextBlock
        {
            Text = "💻 Регистрация переносного устройства",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 8)
        };
        stack.Children.Add(titleBlock);

        var subtitleBlock = new TextBlock
        {
            Text = "Устройство не подключено к домену организации. Для допуска к работе введите ваши данные и инвентарный номер устройства.",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), // Slate 400
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        };
        stack.Children.Add(subtitleBlock);

        // Field 1: ФИО
        stack.Children.Add(new TextBlock
        {
            Text = "ФИО ответственного сотрудника *",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            Margin = new Thickness(0, 0, 0, 4)
        });
        fullNameBox = new TextBox
        {
            FontSize = 14,
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 12),
            Text = !string.IsNullOrWhiteSpace(settings.NonAdFullName) ? settings.NonAdFullName : Environment.UserName
        };
        stack.Children.Add(fullNameBox);

        // Field 2: Инвентарный номер
        stack.Children.Add(new TextBlock
        {
            Text = "Инвентарный номер / Имя ноутбука *",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            Margin = new Thickness(0, 0, 0, 4)
        });
        inventoryBox = new TextBox
        {
            FontSize = 14,
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 12),
            Text = !string.IsNullOrWhiteSpace(settings.NonAdInventoryNumber) ? settings.NonAdInventoryNumber : Environment.MachineName
        };
        stack.Children.Add(inventoryBox);

        // Field 3: Филиал / подразделение
        stack.Children.Add(new TextBlock
        {
            Text = "Филиал или подразделение",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            Margin = new Thickness(0, 0, 0, 4)
        });
        branchBox = new TextBox
        {
            FontSize = 14,
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 12),
            Text = settings.NonAdBranch ?? ""
        };
        stack.Children.Add(branchBox);

        errorText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113)), // Red 400
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
            Visibility = Visibility.Collapsed
        };
        stack.Children.Add(errorText);

        var saveBtn = new Button
        {
            Content = "Зарегистрировать и продолжить",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(16, 10, 16, 10),
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        saveBtn.Click += SaveBtn_Click;
        stack.Children.Add(saveBtn);

        rootBorder.Child = stack;
        Content = rootBorder;
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        string fn = fullNameBox.Text.Trim();
        string inv = inventoryBox.Text.Trim();
        string branch = branchBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(fn) || fn.Length < 3)
        {
            errorText.Text = "Пожалуйста, укажите ваши полные ФИО.";
            errorText.Visibility = Visibility.Visible;
            return;
        }

        if (string.IsNullOrWhiteSpace(inv))
        {
            errorText.Text = "Пожалуйста, укажите инвентарный номер или имя устройства.";
            errorText.Visibility = Visibility.Visible;
            return;
        }

        settings.NonAdRegistered = true;
        settings.NonAdFullName = fn;
        settings.NonAdInventoryNumber = inv;
        settings.NonAdBranch = branch;
        settings.IsPortable = true;
        settings.Save();

        registered = true;
        _ = AntiTheftService.SendTelemetryAsync(settings.ServerUrl ?? "https://helper.gp1.loc");
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!registered && !settings.NonAdRegistered)
        {
            // Block closing until registered
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }
}

