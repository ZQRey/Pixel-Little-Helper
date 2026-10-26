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
        var help = new Expander { Header = "Помощь · команды сообщений", Margin = new Thickness(0,0,0,10), Content = new TextBlock { Text = "/срочно Текст — срочное уведомление поверх окон.\n/танец — помощник получателя танцует.\n/танец Текст — танец и обычный текст.\n/pet (/погладить, /питомец) — помощник гладит кошку или собаку.\nКоманды пишутся в начале и не показываются в переписке. Их можно сочетать: /срочно /танец Текст.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) } }; panel.Children.Add(help);
        var formattingHelp = new Button { Content = "Помощь стилизации", Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(10) };
        formattingHelp.Click += (_, _) => {
            var window = new Window { Title = "Помощь стилизации", Owner = this, Width = 570, Height = 610, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            MessengerDialog.Apply(window, settings);
            window.Content = new ScrollViewer { Content = new TextBlock { Text = MessageFormatting.Help, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20) }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            window.ShowDialog();
        };
        panel.Children.Add(formattingHelp);
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
        string[] displayModes = ["Topmost", "Normal", "Background"];
        var displayMode = Choice("Положение помощника", ["Поверх всех окон", "Обычный режим", "На заднем плане (в фоне)"], Math.Max(0, Array.IndexOf(displayModes, settings.DisplayMode)));
        Toggle("Анимация эмодзи в переписке", settings.AnimatedChatEmojis, v => settings.AnimatedChatEmojis = v);
        Toggle("Реакции помощника на эмодзи", settings.EmojiReactions, v => settings.EmojiReactions = v);
        Toggle("Реакции на входящие эмодзи", settings.IncomingEmojiReactions, v => settings.IncomingEmojiReactions = v);
        Toggle("Звук", settings.ChatSound && SoundManager.NormalizeProfile(settings.SoundProfile) != "Mute", v => settings.ChatSound = v);
        string[] soundProfiles = ["Sound", "VoiceAdult", "VoiceChild", "Mute"];
        string[] soundLabels = ["🔔 Звуковые сигналы (sci-fi)", "🤖 Голос помощника (робот)", "🧸 Голос помощника (детский)", "🔇 Без звука"];
        int selectedSoundProfile = Math.Max(0, Array.IndexOf(soundProfiles, SoundManager.NormalizeProfile(settings.SoundProfile)));

        panel.Children.Add(new TextBlock { Text = "Звуковая схема", Margin = new Thickness(0, 12, 0, 5) });
        var soundDock = new DockPanel();
        var previewBtn = new Button { Content = "▶ Прослушать", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 5, 10, 5), VerticalAlignment = VerticalAlignment.Center, Cursor = System.Windows.Input.Cursors.Hand, IsEnabled = selectedSoundProfile < 3 };
        var soundBox = new ComboBox { ItemsSource = soundLabels, SelectedIndex = selectedSoundProfile, Padding = new Thickness(6) };
        soundBox.SelectionChanged += (_, _) => { previewBtn.IsEnabled = soundBox.SelectedIndex >= 0 && soundBox.SelectedIndex < 3; };
        previewBtn.Click += (_, _) =>
        {
            int idx = soundBox.SelectedIndex;
            if (idx >= 0 && idx < soundProfiles.Length && soundProfiles[idx] != "Mute")
            {
                SoundManager.PlayPreview(soundProfiles[idx], SoundEvent.MessageReceived);
            }
        };
        DockPanel.SetDock(previewBtn, Dock.Right);
        soundDock.Children.Add(previewBtn);
        soundDock.Children.Add(soundBox);
        panel.Children.Add(soundDock);

        Toggle("Показывать текст уведомления", settings.ChatPreview, v => settings.ChatPreview = v);
        Toggle("Уведомления Windows", settings.ChatWindowsNotifications, v => settings.ChatWindowsNotifications = v);
        Toggle("Облачко помощника", settings.ChatComicNotifications, v => settings.ChatComicNotifications = v);
        Toggle("Не беспокоить", settings.ChatDoNotDisturb, v => settings.ChatDoNotDisturb = v);
        panel.Children.Add(new TextBlock { Text = "Срочные рассылки показывают помощника поверх окон на срок до 60 секунд и обходят «Не беспокоить».", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,0) });
        var apply = new Button { Content = "Применить", Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(10), IsDefault = true };
        apply.Click += (_, _) =>
        {
            settings.ChatTheme = themes[theme.SelectedIndex];
            settings.ChatFontSize = sizes[font.SelectedIndex];
            settings.ChatBackground = backgrounds[background.SelectedIndex];
            settings.ChatBackgroundImage = image;
            settings.ChatBackgroundDim = dim.Value;
            settings.DisplayMode = displayModes[displayMode.SelectedIndex];
            string chosenProfile = soundProfiles[soundBox.SelectedIndex];
            settings.SoundProfile = chosenProfile;
            if (chosenProfile == "Mute") settings.ChatSound = false;
            foreach (var save in saves) save();
            if (settings.ChatSound && settings.SoundProfile == "Mute") settings.SoundProfile = "Sound";
            settings.Save();
            if (Application.Current?.MainWindow is PetWindow pet) pet.ApplyDisplayMode();
            DialogResult = true;
        };
        panel.Children.Add(apply);
    }
}
