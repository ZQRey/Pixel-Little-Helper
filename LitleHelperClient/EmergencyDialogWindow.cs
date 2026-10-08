using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelHelper;

public sealed class EmergencyDialogWindow : Window
{
    private readonly HubConnectionService hub;
    private readonly Settings settings;

    public EmergencyDialogWindow(HubConnectionService hub, Settings settings)
    {
        this.hub = hub;
        this.settings = settings;

        Title = Loc.T("ActionEmergency");
        Width = 440;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)); // Slate 900
        Foreground = Brushes.White;

        BuildUi();
    }

    private void BuildUi()
    {
        var panel = new StackPanel { Margin = new Thickness(24) };

        var title = new TextBlock
        {
            Text = "🚨 " + Loc.T("ActionEmergency"),
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113)),
            Margin = new Thickness(0, 0, 0, 16)
        };
        panel.Children.Add(title);

        panel.Children.Add(new TextBlock
        {
            Text = "Выберите код тревоги:",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            Margin = new Thickness(0, 0, 0, 6)
        });

        var codeCombo = new ComboBox
        {
            Height = 36,
            Margin = new Thickness(0, 0, 0, 16),
            FontSize = 14
        };

        var codes = new[]
        {
            ("CODE_RED", "🔴 КОД КРАСНЫЙ (Пожар / Эвакуация)"),
            ("CODE_BLACK", "⚫ КОД ЧЁРНЫЙ (Теракт / Эвакуация)"),
            ("CODE_ORANGE", "🟠 КОД ОРАНЖЕВЫЙ (ЧС техногенного характера)"),
            ("CODE_YELLOW", "🟡 КОД ЖЁЛТЫЙ (Внешнее ЧС / Массовое поступление)"),
            ("CODE_BLUE", "🔵 КОД СИНИЙ (Остановка сердца / СЛР)"),
            ("CODE_WHITE", "⚪ КОД БЕЛЫЙ (Нападение / Охрана)"),
            ("CODE_PINK", "🌸 КОД РОЗОВЫЙ (Потеря ребёнка)")
        };

        foreach (var (val, name) in codes)
        {
            codeCombo.Items.Add(new ComboBoxItem { Content = name, Tag = val });
        }
        codeCombo.SelectedIndex = 0;
        panel.Children.Add(codeCombo);

        panel.Children.Add(new TextBlock
        {
            Text = "Номер кабинета / палаты:",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            Margin = new Thickness(0, 0, 0, 6)
        });

        var cabinetBox = new TextBox
        {
            Text = settings.TicketRoom ?? "",
            Height = 34,
            FontSize = 14,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 16)
        };
        panel.Children.Add(cabinetBox);

        panel.Children.Add(new TextBlock
        {
            Text = "Дополнительные примечания (необязательно):",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            Margin = new Thickness(0, 0, 0, 6)
        });

        var notesBox = new TextBox
        {
            Height = 50,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 20)
        };
        panel.Children.Add(notesBox);

        var sendBtn = new Button
        {
            Content = "🚨 ПЕРЕДАТЬ ТРЕВОГУ ВСЕМ",
            Height = 44,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(220, 38, 38)), // Red 600
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };

        sendBtn.Click += async (_, _) =>
        {
            sendBtn.IsEnabled = false;
            try
            {
                var selectedTag = (codeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "CODE_RED";
                string cab = cabinetBox.Text.Trim();
                string notes = notesBox.Text.Trim();

                await hub.TriggerEmergencyAlertAsync(selectedTag, cab, notes);
                MessageBox.Show(this, "Сигнал экстренного оповещения успешно отправлен!", "Оповещение", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Ошибка передачи оповещения: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                sendBtn.IsEnabled = true;
            }
        };

        panel.Children.Add(sendBtn);
        Content = panel;
    }
}
