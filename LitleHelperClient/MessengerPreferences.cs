using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PixelHelper;
internal sealed class MessengerPreferences : Window
{
    internal MessengerPreferences(Settings settings)
    {
        Title = "Оформление и уведомления"; Width = 440; Height = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MessengerDialog.Apply(this,settings);
        var panel = new StackPanel { Margin = new Thickness(20) }; Content = new ScrollViewer { Content = panel };
        ComboBox Choice(string label, string[] labels, int selected)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 12, 0, 5) });
            var box = new ComboBox { ItemsSource = labels, SelectedIndex = selected, Padding = new Thickness(6) }; panel.Children.Add(box); return box;
        }
        string[] themes = ["Helper", "Light", "Dark", "Contrast"], backgrounds = ["Plain", "Dots", "Grid", "Image"];
        var theme = Choice("Тема", ["Помощник", "Светлая", "Тёмная", "Высокая контрастность"], Math.Max(0, Array.IndexOf(themes, settings.ChatTheme)));
        double[] sizes = [11, 13, 16, 20]; var font = Choice("Размер текста", ["Маленький", "Обычный", "Крупный", "Очень крупный"], Math.Max(0, Array.IndexOf(sizes, settings.ChatFontSize)));
        var background = Choice("Фон чата", ["Однотонный", "Точки", "Сетка", "Изображение"], Math.Max(0, Array.IndexOf(backgrounds, settings.ChatBackground)));
        string? image = settings.ChatBackgroundImage;
        var file = new Button { Content = "Выбрать изображение…", Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(6) };
        file.Click += (_, _) => { var dialog = new OpenFileDialog { Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp" }; if (dialog.ShowDialog(this) == true) { image = dialog.FileName; background.SelectedIndex = 3; file.Content = System.IO.Path.GetFileName(image); } }; panel.Children.Add(file);
        panel.Children.Add(new TextBlock { Text = "Затемнение изображения", Margin = new Thickness(0, 12, 0, 5) });
        var dim = new Slider { Minimum = 0, Maximum = .85, Value = Math.Clamp(settings.ChatBackgroundDim, 0, .85) }; panel.Children.Add(dim);
        var saves = new List<Action>();
        void Toggle(string label, bool value, Action<bool> save) { var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 8, 0, 0) }; panel.Children.Add(box); saves.Add(() => save(box.IsChecked == true)); }
        Toggle("Анимация эмодзи в переписке", settings.AnimatedChatEmojis, v => settings.AnimatedChatEmojis = v);
        Toggle("Реакции помощника на эмодзи", settings.EmojiReactions, v => settings.EmojiReactions = v);
        Toggle("Реакции на входящие эмодзи", settings.IncomingEmojiReactions, v => settings.IncomingEmojiReactions = v);
        Toggle("Звук", settings.ChatSound, v => settings.ChatSound = v);
        Toggle("Показывать текст уведомления", settings.ChatPreview, v => settings.ChatPreview = v);
        Toggle("Уведомления Windows", settings.ChatWindowsNotifications, v => settings.ChatWindowsNotifications = v);
        Toggle("Облачко помощника", settings.ChatComicNotifications, v => settings.ChatComicNotifications = v);
        Toggle("Не беспокоить", settings.ChatDoNotDisturb, v => settings.ChatDoNotDisturb = v);
        Toggle("Срочные сообщения обходят «Не беспокоить»", settings.ChatUrgentOverridesQuiet, v => settings.ChatUrgentOverridesQuiet = v);
        var apply = new Button { Content = "Применить", Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(10), IsDefault = true };
        apply.Click += (_, _) => { settings.ChatTheme = themes[theme.SelectedIndex]; settings.ChatFontSize = sizes[font.SelectedIndex]; settings.ChatBackground = backgrounds[background.SelectedIndex]; settings.ChatBackgroundImage = image; settings.ChatBackgroundDim = dim.Value; foreach (var save in saves) save(); settings.Save(); DialogResult = true; }; panel.Children.Add(apply);
    }
}
