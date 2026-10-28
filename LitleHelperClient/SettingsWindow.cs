using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace PixelHelper;

public sealed class SettingsWindow : Window
{
    private readonly Settings settings;
    private readonly PetWindow petWindow;
    private readonly AntivirusService? antivirus;
    private readonly MailStorageEngine mailStorage;
    private readonly StackPanel navPanel;
    private readonly ScrollViewer contentScroll;
    private readonly TextBlock headingBlock;
    private readonly Dictionary<string, Button> navButtons = new(StringComparer.OrdinalIgnoreCase);
    private string activeTab = "theme";

    public SettingsWindow(Settings settings, PetWindow petWindow, AntivirusService? antivirus, MailStorageEngine mailStorage, string initialTab = "theme")
    {
        this.settings = settings;
        this.petWindow = petWindow;
        this.antivirus = antivirus;
        this.mailStorage = mailStorage;

        Title = Loc.T("SettingsWindowTitle");
        Width = 940;
        Height = 680;
        MinWidth = 820;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
        FontFamily = new FontFamily("Segoe UI");

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left navigation sidebar
        var sideBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            Padding = new Thickness(14, 18, 14, 18)
        };
        var sideStack = new StackPanel();
        headingBlock = new TextBlock
        {
            Text = Loc.T("SettingsWindowHeading"),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(8, 0, 8, 18)
        };
        sideStack.Children.Add(headingBlock);

        navPanel = new StackPanel();
        AddNavButton("theme", Loc.T("SettingsTabTheme"));
        AddNavButton("notifications", Loc.T("SettingsTabNotifications"));
        AddNavButton("browser", Loc.T("SettingsTabBrowser"));
        AddNavButton("antivirus", Loc.T("SettingsTabAntivirus"));
        AddNavButton("language", Loc.T("SettingsTabLanguage"));
        AddNavButton("mail", Loc.T("SettingsTabMail"));

        sideStack.Children.Add(navPanel);
        sideBorder.Child = sideStack;
        Grid.SetColumn(sideBorder, 0);
        root.Children.Add(sideBorder);

        // Right content area
        contentScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(26, 22, 26, 26)
        };
        Grid.SetColumn(contentScroll, 1);
        root.Children.Add(contentScroll);

        Content = root;

        Loc.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Loc.LanguageChanged -= OnLanguageChanged;

        SwitchTab(string.IsNullOrWhiteSpace(initialTab) ? "theme" : initialTab);
    }

    private void OnLanguageChanged()
    {
        Dispatcher.InvokeAsync(() =>
        {
            Title = Loc.T("SettingsWindowTitle");
            headingBlock.Text = Loc.T("SettingsWindowHeading");
            if (navButtons.TryGetValue("theme", out var bTheme)) bTheme.Content = Loc.T("SettingsTabTheme");
            if (navButtons.TryGetValue("notifications", out var bNotif)) bNotif.Content = Loc.T("SettingsTabNotifications");
            if (navButtons.TryGetValue("browser", out var bBrowser)) bBrowser.Content = Loc.T("SettingsTabBrowser");
            if (navButtons.TryGetValue("antivirus", out var bAv)) bAv.Content = Loc.T("SettingsTabAntivirus");
            if (navButtons.TryGetValue("language", out var bLang)) bLang.Content = Loc.T("SettingsTabLanguage");
            if (navButtons.TryGetValue("mail", out var bMail)) bMail.Content = Loc.T("SettingsTabMail");
            SwitchTab(activeTab);
        });
    }

    public void SwitchTab(string tabKey)
    {
        if (!navButtons.ContainsKey(tabKey)) tabKey = "theme";
        activeTab = tabKey;

        foreach (var (key, btn) in navButtons)
        {
            bool active = string.Equals(key, activeTab, StringComparison.OrdinalIgnoreCase);
            btn.Background = active
                ? new SolidColorBrush(Color.FromRgb(37, 99, 235))
                : Brushes.Transparent;
            btn.Foreground = active
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(203, 213, 225));
            btn.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }

        contentScroll.Content = activeTab switch
        {
            "theme" => BuildThemeTab(),
            "notifications" => BuildNotificationsTab(),
            "browser" => BuildBrowserTab(),
            "antivirus" => BuildAntivirusTab(),
            "language" => BuildLanguageTab(),
            "mail" => BuildMailTab(),
            _ => BuildThemeTab()
        };
    }

    private void AddNavButton(string key, string label)
    {
        var btn = new Button
        {
            Content = label,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 6),
            FontSize = 13,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225))
        };
        btn.Click += (_, _) => SwitchTab(key);
        navButtons[key] = btn;
        navPanel.Children.Add(btn);
    }

    // -------------------------------------------------------------------------
    // 1. Тема помощника
    // -------------------------------------------------------------------------
    private UIElement BuildThemeTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader(Loc.T("SettingsThemeHeader"), Loc.T("SettingsThemeSub")));

        var skinCard = CardContainer();
        var skinStack = new StackPanel();
        skinStack.Children.Add(SubTitle(Loc.T("SettingsSkinTitle")));

        string currentSkinId = SkinManager.Instance.CurrentSkinId;
        foreach (var skin in SkinManager.Instance.GetAvailableSkins())
        {
            var rb = new RadioButton
            {
                GroupName = "SkinGroup",
                Content = $"{skin.DisplayName} ({skin.Id})",
                IsChecked = string.Equals(skin.Id, currentSkinId, StringComparison.OrdinalIgnoreCase),
                FontSize = 13.5,
                Margin = new Thickness(0, 6, 0, 6),
                Cursor = Cursors.Hand
            };
            string targetId = skin.Id;
            rb.Checked += (_, _) =>
            {
                if (SkinManager.Instance.SetSkin(targetId))
                {
                    petWindow.React(PetState.Twirl, 1.5);
                }
            };
            skinStack.Children.Add(rb);
        }
        skinCard.Child = skinStack;
        panel.Children.Add(skinCard);

        var modeCard = CardContainer();
        var modeStack = new StackPanel();
        modeStack.Children.Add(SubTitle(Loc.T("SettingsDisplayModeTitle")));

        var modes = new (string Mode, string Title)[]
        {
            ("Topmost", Loc.T("SettingsTopmost")),
            ("Normal", Loc.T("SettingsNormalWindow")),
            ("Background", Loc.T("SettingsBackgroundWindow"))
        };

        foreach (var (mode, title) in modes)
        {
            var rb = new RadioButton
            {
                GroupName = "DisplayModeGroup",
                Content = title,
                IsChecked = string.Equals(settings.DisplayMode, mode, StringComparison.OrdinalIgnoreCase),
                FontSize = 13,
                Margin = new Thickness(0, 5, 0, 5),
                Cursor = Cursors.Hand
            };
            string targetMode = mode;
            rb.Checked += (_, _) => petWindow.SetDisplayMode(targetMode);
            modeStack.Children.Add(rb);
        }

        var chkHidden = new CheckBox
        {
            Content = Loc.T("SettingsHideAssistant"),
            IsChecked = settings.AssistantHidden,
            FontSize = 13,
            Margin = new Thickness(0, 12, 0, 8),
            Cursor = Cursors.Hand
        };
        chkHidden.Click += (_, _) => petWindow.SetAssistantHidden(chkHidden.IsChecked == true);
        modeStack.Children.Add(chkHidden);

        var resetPosBtn = ActionButton("📍 Сбросить позицию помощника по умолчанию", false);
        resetPosBtn.HorizontalAlignment = HorizontalAlignment.Left;
        resetPosBtn.Margin = new Thickness(0, 8, 0, 0);
        resetPosBtn.Click += (_, _) =>
        {
            var work = SystemParameters.WorkArea;
            settings.X = work.Right - 140;
            settings.Y = work.Bottom - 140;
            settings.Save();
            petWindow.RestoreDisplayPreferences();
            petWindow.Left = settings.X.Value;
            petWindow.Top = settings.Y.Value;
        };
        modeStack.Children.Add(resetPosBtn);

        modeCard.Child = modeStack;
        panel.Children.Add(modeCard);

        return panel;
    }

    // -------------------------------------------------------------------------
    // 2. Оповещение и звук
    // -------------------------------------------------------------------------
    private UIElement BuildNotificationsTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader(Loc.T("SettingsNotificationsHeader"), Loc.T("SettingsNotificationsSub")));

        var soundCard = CardContainer();
        var soundStack = new StackPanel();
        soundStack.Children.Add(SubTitle(Loc.T("SettingsSoundProfilesTitle")));

        string currentProfile = SoundManager.NormalizeProfile(settings.SoundProfile);
        var profiles = new (string Key, string Label)[]
        {
            ("Sound", Loc.T("SoundProfileDefault")),
            ("VoiceAdult", Loc.T("SoundProfileVoiceAdult")),
            ("VoiceChild", Loc.T("SoundProfileVoiceChild")),
            ("Mute", Loc.T("SoundProfileMute"))
        };

        foreach (var (profKey, label) in profiles)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            var rb = new RadioButton
            {
                GroupName = "SoundProfileGroup",
                Content = label,
                IsChecked = string.Equals(currentProfile, profKey, StringComparison.OrdinalIgnoreCase),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 240,
                Cursor = Cursors.Hand
            };
            string targetProf = profKey;
            rb.Checked += (_, _) =>
            {
                settings.SoundProfile = targetProf;
                settings.ChatSound = targetProf != "Mute";
                settings.Save();
            };
            row.Children.Add(rb);

            if (profKey != "Mute")
            {
                var previewBtn = ActionButton("▶ Прослушать", false);
                previewBtn.Padding = new Thickness(10, 3, 10, 3);
                previewBtn.Click += (_, _) => SoundManager.PlayPreview(targetProf, SoundEvent.MessageReceived);
                row.Children.Add(previewBtn);
            }
            soundStack.Children.Add(row);
        }
        soundCard.Child = soundStack;
        panel.Children.Add(soundCard);

        var flagsCard = CardContainer();
        var flagsStack = new StackPanel();
        flagsStack.Children.Add(SubTitle("Режим уведомлений"));

        flagsStack.Children.Add(MakeSettingCheck("Режим «Не беспокоить» (приглушить обычные уведомления)", settings.ChatDoNotDisturb, v => settings.ChatDoNotDisturb = v));
        flagsStack.Children.Add(MakeSettingCheck("Разрешать срочным сообщениям пробивать режим «Не беспокоить»", settings.ChatUrgentOverridesQuiet, v => settings.ChatUrgentOverridesQuiet = v));
        flagsStack.Children.Add(MakeSettingCheck("Показывать облачка сообщений над помощником", settings.ChatComicNotifications, v => settings.ChatComicNotifications = v));
        flagsStack.Children.Add(MakeSettingCheck("Показывать предпросмотр текста сообщения в уведомлении", settings.ChatPreview, v => settings.ChatPreview = v));
        flagsStack.Children.Add(MakeSettingCheck("Системные всплывающие уведомления в трее Windows", settings.ChatWindowsNotifications, v => settings.ChatWindowsNotifications = v));
        flagsStack.Children.Add(MakeSettingCheck("Уведомлять о новых письмах почты даже когда помощник скрыт", settings.MailNotifyWhenHidden, v => settings.MailNotifyWhenHidden = v));
        flagsStack.Children.Add(MakeSettingCheck("Напоминания о гимнастике для глаз (раз в 50 минут)", settings.EnableEyeCareReminders, v => settings.EnableEyeCareReminders = v));

        flagsCard.Child = flagsStack;
        panel.Children.Add(flagsCard);

        var medCard = CardContainer();
        var medStack = new StackPanel();
        medStack.Children.Add(SubTitle("🩺 Медицинский рабочий статус"));

        foreach (var status in settings.MedicalStatuses.Where(s => s.IsActive).OrderBy(s => s.OrderIndex))
        {
            var rb = new RadioButton
            {
                GroupName = "MedStatusGroup",
                Content = $"{status.Emoji} {status.Title}",
                IsChecked = string.Equals(settings.MedicalWorkStatus, status.Key, StringComparison.OrdinalIgnoreCase),
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 4),
                Cursor = Cursors.Hand
            };
            string key = status.Key;
            rb.Checked += (_, _) =>
            {
                settings.MedicalWorkStatus = key;
                settings.Save();
            };
            medStack.Children.Add(rb);
        }

        medCard.Child = medStack;
        panel.Children.Add(medCard);

        return panel;
    }

    // -------------------------------------------------------------------------
    // 3. Основной браузер
    // -------------------------------------------------------------------------
    private UIElement BuildBrowserTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader(Loc.T("SettingsBrowserHeader"), Loc.T("SettingsBrowserSub")));

        var card = CardContainer();
        var stack = new StackPanel();
        stack.Children.Add(SubTitle(Loc.T("SettingsInstalledBrowsers")));

        var browsers = BrowserDetector.GetInstalledBrowsers();
        var statusLabel = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(settings.PreferredBrowserName)
                ? "Текущий браузер: системный по умолчанию"
                : $"Текущий браузер: {settings.PreferredBrowserName} ({settings.PreferredBrowserPath})",
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            Margin = new Thickness(0, 0, 0, 12)
        };
        stack.Children.Add(statusLabel);

        foreach (var b in browsers)
        {
            var rb = new RadioButton
            {
                GroupName = "BrowserGroup",
                Content = $"{b.Name}  —  {b.ExecutablePath}",
                IsChecked = string.Equals(settings.PreferredBrowserPath, b.ExecutablePath, StringComparison.OrdinalIgnoreCase),
                FontSize = 13,
                Margin = new Thickness(0, 5, 0, 5),
                Cursor = Cursors.Hand
            };
            var browserInfo = b;
            rb.Checked += (_, _) =>
            {
                settings.PreferredBrowserPath = browserInfo.ExecutablePath;
                settings.PreferredBrowserName = browserInfo.Name;
                settings.BrowserPromptShown = true;
                settings.Save();
                statusLabel.Text = $"Текущий браузер: {browserInfo.Name} ({browserInfo.ExecutablePath})";
            };
            stack.Children.Add(rb);
        }

        var customBtn = ActionButton("📂 Выбрать другой исполняемый файл браузера (.exe)…", false);
        customBtn.HorizontalAlignment = HorizontalAlignment.Left;
        customBtn.Margin = new Thickness(0, 12, 0, 0);
        customBtn.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog
            {
                Title = "Выберите исполняемый файл браузера",
                Filter = "Программы (*.exe)|*.exe"
            };
            if (dlg.ShowDialog(this) == true)
            {
                settings.PreferredBrowserPath = dlg.FileName;
                settings.PreferredBrowserName = Path.GetFileNameWithoutExtension(dlg.FileName);
                settings.BrowserPromptShown = true;
                settings.Save();
                SwitchTab("browser");
            }
        };
        stack.Children.Add(customBtn);

        card.Child = stack;
        panel.Children.Add(card);
        return panel;
    }

    // -------------------------------------------------------------------------
    // 4. Антивирус PixelGuard AV
    // -------------------------------------------------------------------------
    private UIElement BuildAntivirusTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader(Loc.T("SettingsAntivirusHeader"), Loc.T("SettingsAntivirusSub")));

        var statusCard = CardContainer();
        var statusStack = new StackPanel();
        statusStack.Children.Add(SubTitle("Модули защиты PixelGuard AV"));

        statusStack.Children.Add(MakeSettingCheck("Включить антивирусную защиту файлов и накопителей (PixelGuard AV)", settings.AntivirusEnabled, v => settings.AntivirusEnabled = v));
        statusStack.Children.Add(MakeSettingCheck("Защита браузера от фишинговых и вредоносных ссылок (WebGuard)", settings.WebGuardEnabled, v => settings.WebGuardEnabled = v));
        statusStack.Children.Add(MakeSettingCheck("Проверять входящие письма и вложения на вирусы", settings.MailScanAntivirus, v => settings.MailScanAntivirus = v));
        statusStack.Children.Add(MakeSettingCheck("Проверять входящие письма на фишинговые адреса и подмену домена", settings.MailScanPhishing, v => settings.MailScanPhishing = v));

        var actionsRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var scanBtn = ActionButton("🔍 Проверить папку или диск…", true);
        scanBtn.Margin = new Thickness(0, 0, 10, 0);
        scanBtn.Click += async (_, _) =>
        {
            if (antivirus == null) return;
            var dlg = new OpenFolderDialog { Title = "Выберите папку или диск для проверки PixelGuard AV" };
            if (dlg.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
            {
                scanBtn.IsEnabled = false;
                scanBtn.Content = "⏳ Сканирование…";
                try
                {
                    var summary = await antivirus.ScanDirectoryAsync(dlg.FolderName, null, CancellationToken.None);
                    MessageBox.Show(this,
                        $"Проверено файлов: {summary.TotalFilesScanned}\nОбнаружено угроз: {summary.ThreatsDetected}",
                        "PixelGuard AV",
                        MessageBoxButton.OK,
                        summary.ThreatsDetected > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                    SwitchTab("antivirus");
                }
                finally
                {
                    scanBtn.IsEnabled = true;
                    scanBtn.Content = "🔍 Проверить папку или диск…";
                }
            }
        };
        actionsRow.Children.Add(scanBtn);

        var updateDbBtn = ActionButton("🔄 Обновить антивирусные базы", false);
        updateDbBtn.Click += async (_, _) =>
        {
            if (antivirus == null) return;
            updateDbBtn.IsEnabled = false;
            updateDbBtn.Content = "⏳ Обновление…";
            try
            {
                await antivirus.CheckAndUpdateDatabasesAsync(CancellationToken.None);
                MessageBox.Show(this, "Проверка обновлений сигнатур PixelGuard AV завершена.", "PixelGuard AV", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                updateDbBtn.IsEnabled = true;
                updateDbBtn.Content = "🔄 Обновить антивирусные базы";
            }
        };
        actionsRow.Children.Add(updateDbBtn);

        statusStack.Children.Add(actionsRow);
        statusCard.Child = statusStack;
        panel.Children.Add(statusCard);

        // Quarantine Card
        var qCard = CardContainer();
        var qStack = new StackPanel();
        qStack.Children.Add(SubTitle("🔒 Карантин изолированных файлов"));

        var qItems = antivirus?.Quarantine.GetQuarantinedItems() ?? [];
        if (qItems.Count == 0)
        {
            qStack.Children.Add(new TextBlock
            {
                Text = "В карантине нет изолированных объектов.",
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 4, 0, 4)
            });
        }
        else
        {
            foreach (var item in qItems.OrderByDescending(i => i.QuarantinedAt))
            {
                var row = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(254, 242, 242)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(254, 202, 202)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel();
                info.Children.Add(new TextBlock
                {
                    Text = $"{item.ThreatName} ({item.ThreatType})",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(153, 27, 27)),
                    FontSize = 12.5
                });
                info.Children.Add(new TextBlock
                {
                    Text = $"{item.OriginalPath} · {item.QuarantinedAt.ToLocalTime():dd.MM.yyyy HH:mm}",
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                Grid.SetColumn(info, 0);
                grid.Children.Add(info);

                var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var restoreBtn = ActionButton("Восстановить", false);
                restoreBtn.Padding = new Thickness(8, 3, 8, 3);
                restoreBtn.Margin = new Thickness(0, 0, 6, 0);
                var captured = item;
                restoreBtn.Click += (_, _) =>
                {
                    antivirus?.Quarantine.RestoreFile(captured.Id);
                    SwitchTab("antivirus");
                };
                btns.Children.Add(restoreBtn);

                var delBtn = ActionButton("Удалить навсегда", false);
                delBtn.Padding = new Thickness(8, 3, 8, 3);
                delBtn.Click += (_, _) =>
                {
                    antivirus?.Quarantine.DeletePermanently(captured.Id);
                    SwitchTab("antivirus");
                };
                btns.Children.Add(delBtn);

                Grid.SetColumn(btns, 1);
                grid.Children.Add(btns);
                row.Child = grid;
                qStack.Children.Add(row);
            }
        }

        qCard.Child = qStack;
        panel.Children.Add(qCard);

        return panel;
    }

    // -------------------------------------------------------------------------
    // 5. Язык
    // -------------------------------------------------------------------------
    private UIElement BuildLanguageTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader(Loc.T("SettingsLanguageHeader"), Loc.T("SettingsLanguageSub")));

        var card = CardContainer();
        var stack = new StackPanel();
        stack.Children.Add(SubTitle(Loc.T("SettingsAvailableLanguages")));

        foreach (var lang in Enum.GetValues<AppLanguage>())
        {
            var rb = new RadioButton
            {
                GroupName = "AppLangGroup",
                Content = Loc.DisplayName(lang),
                IsChecked = Loc.CurrentLanguage == lang,
                FontSize = 14,
                Margin = new Thickness(0, 6, 0, 6),
                Cursor = Cursors.Hand
            };
            var targetLang = lang;
            rb.Checked += (_, _) =>
            {
                Loc.CurrentLanguage = targetLang;
                settings.Language = Loc.Code;
                settings.Save();
                petWindow.React(PetState.Twirl, 1.2);
            };
            stack.Children.Add(rb);
        }

        card.Child = stack;
        panel.Children.Add(card);
        return panel;
    }

    // -------------------------------------------------------------------------
    // 6. Настройка почтового клиента
    // -------------------------------------------------------------------------
    private UIElement BuildMailTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader(
            Loc.T("SettingsMailHeader"),
            Loc.T("SettingsMailSub")));

        // Connected accounts card
        var accountsCard = CardContainer();
        var accountsStack = new StackPanel();

        var topHeaderRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        topHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var accTitle = SubTitle(Loc.Format("SettingsConnectedAccounts", settings.MailAccounts.Count));
        Grid.SetColumn(accTitle, 0);
        topHeaderRow.Children.Add(accTitle);

        var openMailBtn = ActionButton(Loc.T("SettingsOpenMailClient"), true);
        openMailBtn.Click += (_, _) => petWindow.OpenMailWindow();
        Grid.SetColumn(openMailBtn, 1);
        topHeaderRow.Children.Add(openMailBtn);
        accountsStack.Children.Add(topHeaderRow);

        if (settings.MailAccounts.Count == 0)
        {
            accountsStack.Children.Add(new TextBlock
            {
                Text = Loc.T("SettingsNoAccounts"),
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 0, 0, 8)
            });
        }
        else
        {
            foreach (var acc in settings.MailAccounts.ToList())
            {
                var rowBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var infoStack = new StackPanel();
                string modeLabel = acc.StorageMode == MailStorageMode.DownloadAllToComputer
                    ? "💻 Выгрузка на ПК"
                    : "☁️ Хранить на сервере";
                string formatLabel = acc.OffloadFormat == MailOffloadFormat.SingleFileMbox
                    ? "📦 Сгрузка: архив .mbox"
                    : "📄 Сгрузка: файлы .eml";
                infoStack.Children.Add(new TextBlock
                {
                    Text = $"{acc.EmailAddress} {(acc.IsDefault ? "★ (По умолчанию)" : "")}",
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 13.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
                });
                infoStack.Children.Add(new TextBlock
                {
                    Text = $"{acc.IncomingProtocol} {acc.IncomingHost}:{acc.IncomingPort} ({acc.IncomingSslMode})  ·  SMTP {acc.SmtpHost}:{acc.SmtpPort}  ·  {modeLabel}  ·  {formatLabel}",
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    Margin = new Thickness(0, 2, 0, 0)
                });
                Grid.SetColumn(infoStack, 0);
                rowGrid.Children.Add(infoStack);

                var btnRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var captured = acc;

                var fmtBtn = ActionButton(acc.OffloadFormat == MailOffloadFormat.SingleFileMbox ? "📦 Формат: .mbox" : "📄 Формат: .eml", false);
                fmtBtn.Padding = new Thickness(8, 4, 8, 4);
                fmtBtn.Margin = new Thickness(0, 0, 6, 0);
                fmtBtn.ToolTip = "Переключить формат сгрузки: по отдельности (.eml) или единым файлом (.mbox)";
                fmtBtn.Click += (_, _) =>
                {
                    captured.OffloadFormat = captured.OffloadFormat == MailOffloadFormat.SingleFileMbox
                        ? MailOffloadFormat.IndividualEml
                        : MailOffloadFormat.SingleFileMbox;
                    settings.Save();
                    SwitchTab("mail");
                };
                btnRow.Children.Add(fmtBtn);

                var offloadBtn = ActionButton("💻 Сгрузить на ПК", false);
                offloadBtn.Padding = new Thickness(8, 4, 8, 4);
                offloadBtn.Margin = new Thickness(0, 0, 6, 0);
                offloadBtn.Click += async (_, _) =>
                {
                    offloadBtn.IsEnabled = false;
                    offloadBtn.Content = "⏳ Выгрузка…";
                    try
                    {
                        await mailStorage.SyncAccountAsync(captured, CancellationToken.None);
                        var (count, destPath) = mailStorage.OffloadAccountToComputer(
                            captured.Id,
                            format: captured.OffloadFormat,
                            accountEmail: captured.EmailAddress);

                        string formatText = captured.OffloadFormat == MailOffloadFormat.SingleFileMbox
                            ? "в единый архив (.mbox)"
                            : "по отдельности (файлы .eml)";

                        if (MessageBox.Show(this,
                            $"Сгрузка писем завершена!\n\nФормат: {formatText}\nСохранено писем: {count}\nПапка / файл:\n{destPath}\n\nОткрыть расположение в Проводнике?",
                            "Выгрузка писем на ПК",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information) == MessageBoxResult.Yes)
                        {
                            string folderToOpen = Directory.Exists(destPath) ? destPath : (Path.GetDirectoryName(destPath) ?? destPath);
                            Process.Start(new ProcessStartInfo("explorer.exe", folderToOpen) { UseShellExecute = true });
                        }
                        SwitchTab("mail");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, ex.Message, "Ошибка выгрузки", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    finally
                    {
                        offloadBtn.IsEnabled = true;
                        offloadBtn.Content = "💻 Сгрузить на ПК";
                    }
                };
                btnRow.Children.Add(offloadBtn);

                var purgeBtn = ActionButton("🧹 Сгрузить и очистить сервер", false);
                purgeBtn.Padding = new Thickness(8, 4, 8, 4);
                purgeBtn.Margin = new Thickness(0, 0, 6, 0);
                purgeBtn.ToolTip = "Сгрузить все письма на ПК в папку архива и полностью удалить их с почтового сервера";
                purgeBtn.Click += async (_, _) =>
                {
                    var confirm = MessageBox.Show(this,
                        $"ВНИМАНИЕ!\n\nВсе письма ящика «{captured.EmailAddress}» будут скачаны на этот компьютер и ПОЛНОСТЬЮ УДАЛЕНЫ с почтового сервера.\n\nНа сервере ящик станет пустым, а все ваши письма сохранятся на этом ПК.\n\nВы действительно хотите продолжить?",
                        "Сгрузить на ПК и очистить сервер",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (confirm != MessageBoxResult.Yes) return;

                    purgeBtn.IsEnabled = false;
                    purgeBtn.Content = "⏳ Очистка…";
                    try
                    {
                        var (count, destPath) = await mailStorage.OffloadAndPurgeAccountAsync(captured, CancellationToken.None);
                        if (MessageBox.Show(this,
                            $"Операция успешно завершена!\n\nСгружено писем на ПК: {count}\nПочтовый сервер очищен.\nРасположение:\n{destPath}\n\nОткрыть папку с сохраненными письмами?",
                            "Сервер очищен",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information) == MessageBoxResult.Yes)
                        {
                            string folderToOpen = Directory.Exists(destPath) ? destPath : (Path.GetDirectoryName(destPath) ?? destPath);
                            Process.Start(new ProcessStartInfo("explorer.exe", folderToOpen) { UseShellExecute = true });
                        }
                        SwitchTab("mail");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Ошибка: " + ex.Message, "Сгрузка и очистка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        purgeBtn.IsEnabled = true;
                        purgeBtn.Content = "🧹 Сгрузить и очистить сервер";
                    }
                };
                btnRow.Children.Add(purgeBtn);

                var cloudBtn = ActionButton("☁️ Сгрузить/Скачать PixelMail", false);
                cloudBtn.Padding = new Thickness(8, 4, 8, 4);
                cloudBtn.Margin = new Thickness(0, 0, 6, 0);
                cloudBtn.ToolTip = "Резервная копия и восстановление всех писем через сервер помощника под защитой пароля";
                cloudBtn.Click += (_, _) =>
                {
                    OpenPixelMailCloudDialog(captured);
                };
                btnRow.Children.Add(cloudBtn);

                var delAccBtn = ActionButton("🗑 Удалить", false);
                delAccBtn.Padding = new Thickness(8, 4, 8, 4);
                delAccBtn.Click += (_, _) =>
                {
                    settings.MailAccounts.RemoveAll(a => a.Id == captured.Id);
                    if (settings.MailAccounts.Count > 0 && !settings.MailAccounts.Any(a => a.IsDefault))
                        settings.MailAccounts[0].IsDefault = true;
                    settings.Save();
                    SwitchTab("mail");
                };
                btnRow.Children.Add(delAccBtn);

                Grid.SetColumn(btnRow, 1);
                rowGrid.Children.Add(btnRow);
                rowBorder.Child = rowGrid;
                accountsStack.Children.Add(rowBorder);
            }
        }

        accountsCard.Child = accountsStack;
        panel.Children.Add(accountsCard);

        // Add / Configure Mailbox Wizard Card
        var formCard = CardContainer();
        var formStack = new StackPanel();
        formStack.Children.Add(SubTitle(Loc.T("SettingsAddAccountTitle")));

        var emailBox = MakeTextBox("user@company.kz");
        var passBox = new PasswordBox { Padding = new Thickness(8, 6, 8, 6), FontSize = 13, Margin = new Thickness(0, 0, 0, 8) };
        var nameBox = MakeTextBox(Environment.UserName);

        formStack.Children.Add(FieldLabel("E-mail адрес:"));
        formStack.Children.Add(emailBox);
        formStack.Children.Add(FieldLabel("Пароль (или пароль приложения):"));
        formStack.Children.Add(passBox);
        formStack.Children.Add(FieldLabel("Отображаемое имя отправителя:"));
        formStack.Children.Add(nameBox);

        // Storage mode radio buttons
        formStack.Children.Add(FieldLabel("Режим хранения писем:"));
        var rbKeepServer = new RadioButton
        {
            GroupName = "MailStorageModeGroup",
            Content = "☁️ Оставлять письма на основном сервере (синхронизировать заголовки и кэш)",
            IsChecked = true,
            FontSize = 12.5,
            Margin = new Thickness(0, 3, 0, 3)
        };
        var rbDownloadAll = new RadioButton
        {
            GroupName = "MailStorageModeGroup",
            Content = "💻 Сгружать все письма и вложения на компьютер пользователя (в зашифрованное локальное хранилище)",
            FontSize = 12.5,
            Margin = new Thickness(0, 3, 0, 3)
        };
        var chkDeleteServer = new CheckBox
        {
            Content = "Удалять письма с сервера после полной выгрузки на компьютер",
            FontSize = 12,
            Margin = new Thickness(22, 2, 0, 8)
        };
        formStack.Children.Add(rbKeepServer);
        formStack.Children.Add(rbDownloadAll);
        formStack.Children.Add(chkDeleteServer);

        formStack.Children.Add(FieldLabel("Формат сгрузки писем на ПК:"));
        var rbFormatEml = new RadioButton
        {
            GroupName = "MailOffloadFormatGroup",
            Content = "📄 По отдельности (каждое письмо в отдельном файле .eml)",
            IsChecked = true,
            FontSize = 12.5,
            Margin = new Thickness(0, 3, 0, 3)
        };
        var rbFormatMbox = new RadioButton
        {
            GroupName = "MailOffloadFormatGroup",
            Content = "📦 Одним файлом (.mbox — общий контейнер архива со всеми письмами)",
            FontSize = 12.5,
            Margin = new Thickness(0, 3, 0, 8)
        };
        formStack.Children.Add(rbFormatEml);
        formStack.Children.Add(rbFormatMbox);

        // Protocol & Port boxes (auto-filled by Autodiscover)
        var protoCombo = new ComboBox { Width = 120, Margin = new Thickness(0, 0, 8, 0), SelectedIndex = 0 };
        protoCombo.Items.Add("IMAP");
        protoCombo.Items.Add("POP3");

        var inHostBox = MakeTextBox("imap.domain.kz", 190);
        var inPortBox = MakeTextBox("993", 70);
        var smtpHostBox = MakeTextBox("smtp.domain.kz", 190);
        var smtpPortBox = MakeTextBox("465", 70);

        var serverGrid = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        serverGrid.Children.Add(LabeledControl("Протокол:", protoCombo));
        serverGrid.Children.Add(LabeledControl("Входящий сервер:", inHostBox));
        serverGrid.Children.Add(LabeledControl("Порт:", inPortBox));
        serverGrid.Children.Add(LabeledControl("SMTP сервер:", smtpHostBox));
        serverGrid.Children.Add(LabeledControl("SMTP порт:", smtpPortBox));
        formStack.Children.Add(serverGrid);

        var discoverStatus = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            Margin = new Thickness(0, 2, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        formStack.Children.Add(discoverStatus);

        var wizardBtns = new WrapPanel();
        var autoBtn = ActionButton(Loc.T("SettingsAutodiscoverBtn"), false);
        autoBtn.Margin = new Thickness(0, 0, 8, 0);
        autoBtn.Click += async (_, _) =>
        {
            string email = emailBox.Text.Trim();
            if (!email.Contains('@'))
            {
                discoverStatus.Text = "Введите корректный E-mail адрес для автоопределения.";
                return;
            }
            autoBtn.IsEnabled = false;
            discoverStatus.Text = "⏳ Сканирование портов (993, 143, 995, 110, 465, 587, 25) и проверка SSL/STARTTLS сертификатов…";
            try
            {
                var res = await MailAutodiscover.DiscoverAsync(email, CancellationToken.None);
                protoCombo.SelectedIndex = res.IncomingProtocol == MailIncomingProtocol.Pop3 ? 1 : 0;
                inHostBox.Text = res.IncomingHost;
                inPortBox.Text = res.IncomingPort.ToString();
                smtpHostBox.Text = res.SmtpHost;
                smtpPortBox.Text = res.SmtpPort.ToString();
                discoverStatus.Text = $"✅ {res.Message}";
            }
            catch (Exception ex)
            {
                discoverStatus.Text = $"⚠️ Ошибка автоопределения: {ex.Message}";
            }
            finally
            {
                autoBtn.IsEnabled = true;
            }
        };
        wizardBtns.Children.Add(autoBtn);

        var saveAccBtn = ActionButton(Loc.T("SettingsSaveAccountBtn"), true);
        saveAccBtn.Click += async (_, _) =>
        {
            string email = emailBox.Text.Trim();
            if (!email.Contains('@'))
            {
                discoverStatus.Text = "⚠️ Укажите корректный адрес электронной почты.";
                return;
            }

            if (string.IsNullOrWhiteSpace(inHostBox.Text) || inHostBox.Text == "imap.domain.kz")
            {
                var auto = await MailAutodiscover.DiscoverAsync(email, CancellationToken.None);
                inHostBox.Text = auto.IncomingHost;
                inPortBox.Text = auto.IncomingPort.ToString();
                smtpHostBox.Text = auto.SmtpHost;
                smtpPortBox.Text = auto.SmtpPort.ToString();
            }

            int.TryParse(inPortBox.Text.Trim(), out int inPort);
            int.TryParse(smtpPortBox.Text.Trim(), out int smtpPort);
            if (inPort <= 0) inPort = 993;
            if (smtpPort <= 0) smtpPort = 465;

            var acc = new MailAccountConfig
            {
                EmailAddress = email,
                DisplayName = string.IsNullOrWhiteSpace(nameBox.Text) ? email : nameBox.Text.Trim(),
                Username = email,
                EncryptedPassword = MailCryptoService.ProtectSecret(passBox.Password),
                IncomingProtocol = protoCombo.SelectedIndex == 1 ? MailIncomingProtocol.Pop3 : MailIncomingProtocol.Imap,
                IncomingHost = inHostBox.Text.Trim(),
                IncomingPort = inPort,
                IncomingSslMode = inPort is 993 or 995 ? MailSslMode.ImplicitSsl : MailSslMode.StartTls,
                SmtpHost = smtpHostBox.Text.Trim(),
                SmtpPort = smtpPort,
                SmtpSslMode = smtpPort == 465 ? MailSslMode.ImplicitSsl : MailSslMode.StartTls,
                StorageMode = rbDownloadAll.IsChecked == true ? MailStorageMode.DownloadAllToComputer : MailStorageMode.KeepOnServer,
                DeleteFromServerAfterDownload = chkDeleteServer.IsChecked == true,
                OffloadFormat = rbFormatMbox.IsChecked == true ? MailOffloadFormat.SingleFileMbox : MailOffloadFormat.IndividualEml,
                IsDefault = settings.MailAccounts.Count == 0
            };

            settings.MailAccounts.RemoveAll(a => string.Equals(a.EmailAddress, acc.EmailAddress, StringComparison.OrdinalIgnoreCase));
            settings.MailAccounts.Add(acc);
            settings.Save();

            _ = Task.Run(() => mailStorage.SyncAccountAsync(acc, CancellationToken.None));
            SwitchTab("mail");
        };
        wizardBtns.Children.Add(saveAccBtn);

        formStack.Children.Add(wizardBtns);
        formCard.Child = formStack;
        panel.Children.Add(formCard);

        // Encryption & PixelGuard Mail Filter Card
        var secCard = CardContainer();
        var secStack = new StackPanel();
        secStack.Children.Add(SubTitle(Loc.T("SettingsEncryptionTitle")));

        secStack.Children.Add(FieldLabel("Корпоративный ключ сквозного шифрования писем (Pixel-AES-256):"));
        var keyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var keyBox = MakeTextBox("Введите общий корпоративный ключ или сгенерируйте новый", 420);
        keyBox.Text = MailCryptoService.UnprotectSecret(settings.MailSharedEncryptionKey);
        keyRow.Children.Add(keyBox);

        var saveKeyBtn = ActionButton("Сохранить ключ", false);
        saveKeyBtn.Margin = new Thickness(8, 0, 0, 0);
        saveKeyBtn.Click += (_, _) =>
        {
            settings.MailSharedEncryptionKey = string.IsNullOrWhiteSpace(keyBox.Text)
                ? ""
                : MailCryptoService.ProtectSecret(keyBox.Text.Trim());
            settings.Save();
            MessageBox.Show(this, "Ключ шифрования писем сохранён (защищён через Windows DPAPI).", "Pixel Mail", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        keyRow.Children.Add(saveKeyBtn);
        secStack.Children.Add(keyRow);

        secStack.Children.Add(MakeSettingCheck("Антивирусная проверка входящих писем и архивов вложений (PixelGuard AV)", settings.MailScanAntivirus, v => settings.MailScanAntivirus = v));
        secStack.Children.Add(MakeSettingCheck("Защита от фишинговых адресов, подмены отправителя и ссылок (WebGuard)", settings.MailScanPhishing, v => settings.MailScanPhishing = v));
        secStack.Children.Add(MakeSettingCheck("Автоматическая фильтрация спама в папку «Спам»", settings.MailFilterSpam, v => settings.MailFilterSpam = v));

        // Blacklist / Whitelist editors
        var listsGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        listsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        listsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        listsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var blackPanel = BuildAddressListEditor(
            Loc.T("SettingsBlacklistTitle"),
            settings.MailBlockedSenders,
            entry =>
            {
                MailSecurityScanner.AddToBlacklist(entry, settings);
                mailStorage.ApplyFilterListsToExistingMessages();
                SwitchTab("mail");
            },
            entry =>
            {
                MailSecurityScanner.RemoveFromBlacklist(entry, settings);
                mailStorage.ApplyFilterListsToExistingMessages();
                SwitchTab("mail");
            });
        Grid.SetColumn(blackPanel, 0);
        listsGrid.Children.Add(blackPanel);

        var whitePanel = BuildAddressListEditor(
            Loc.T("SettingsWhitelistTitle"),
            settings.MailTrustedSenders,
            entry =>
            {
                MailSecurityScanner.AddToWhitelist(entry, settings);
                mailStorage.ApplyFilterListsToExistingMessages();
                SwitchTab("mail");
            },
            entry =>
            {
                MailSecurityScanner.RemoveFromWhitelist(entry, settings);
                mailStorage.ApplyFilterListsToExistingMessages();
                SwitchTab("mail");
            });
        Grid.SetColumn(whitePanel, 2);
        listsGrid.Children.Add(whitePanel);

        secStack.Children.Add(listsGrid);
        secCard.Child = secStack;
        panel.Children.Add(secCard);

        return panel;
    }

    private UIElement BuildAddressListEditor(string title, List<string> items, Action<string> onAdd, Action<string> onRemove)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var addRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var input = new TextBox
        {
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 12,
            ToolTip = "email@domain.com или *@domain.com"
        };
        Grid.SetColumn(input, 0);
        addRow.Children.Add(input);

        var addBtn = ActionButton("＋", true);
        addBtn.Padding = new Thickness(10, 3, 10, 3);
        addBtn.Margin = new Thickness(6, 0, 0, 0);
        addBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(input.Text))
                onAdd(input.Text.Trim());
        };
        Grid.SetColumn(addBtn, 1);
        addRow.Children.Add(addBtn);
        stack.Children.Add(addRow);

        if (items.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Список пуст",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184))
            });
        }
        else
        {
            foreach (var entry in items.ToList())
            {
                var itemGrid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                itemGrid.Children.Add(new TextBlock
                {
                    Text = entry,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                var rmBtn = new Button
                {
                    Content = "✕",
                    FontSize = 11,
                    Padding = new Thickness(5, 1, 5, 1),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                    Cursor = Cursors.Hand
                };
                string captured = entry;
                rmBtn.Click += (_, _) => onRemove(captured);
                Grid.SetColumn(rmBtn, 1);
                itemGrid.Children.Add(rmBtn);
                stack.Children.Add(itemGrid);
            }
        }

        border.Child = stack;
        return border;
    }

    // -------------------------------------------------------------------------
    // UI Helpers
    // -------------------------------------------------------------------------
    private static UIElement SectionHeader(string title, string subtitle)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
        });
        stack.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        return stack;
    }

    private static Border CardContainer() => new()
    {
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(18, 16, 18, 16),
        Margin = new Thickness(0, 0, 0, 14)
    };

    private static TextBlock SubTitle(string text) => new()
    {
        Text = text,
        FontSize = 14.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
        Margin = new Thickness(0, 0, 0, 10)
    };

    private static TextBlock FieldLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
        Margin = new Thickness(0, 4, 0, 4)
    };

    private static TextBox MakeTextBox(string placeholder, double width = double.NaN) => new()
    {
        Text = placeholder,
        Width = width,
        Padding = new Thickness(8, 5, 8, 5),
        FontSize = 13,
        Margin = new Thickness(0, 0, 8, 8)
    };

    private static UIElement LabeledControl(string label, FrameworkElement control)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 10, 4) };
        sp.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            Margin = new Thickness(0, 0, 0, 2)
        });
        sp.Children.Add(control);
        return sp;
    }

    private CheckBox MakeSettingCheck(string label, bool initial, Action<bool> setter)
    {
        var chk = new CheckBox
        {
            Content = label,
            IsChecked = initial,
            FontSize = 13,
            Margin = new Thickness(0, 5, 0, 5),
            Cursor = Cursors.Hand
        };
        chk.Click += (_, _) =>
        {
            setter(chk.IsChecked == true);
            settings.Save();
        };
        return chk;
    }

    private static Button ActionButton(string text, bool primary) => new()
    {
        Content = text,
        Padding = new Thickness(12, 6, 12, 6),
        FontSize = 12.5,
        FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
        Background = primary
            ? new SolidColorBrush(Color.FromRgb(37, 99, 235))
            : new SolidColorBrush(Color.FromRgb(241, 245, 249)),
        Foreground = primary
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(30, 41, 59)),
        BorderBrush = primary
            ? new SolidColorBrush(Color.FromRgb(29, 78, 216))
            : new SolidColorBrush(Color.FromRgb(203, 213, 225)),
        BorderThickness = new Thickness(1),
        Cursor = Cursors.Hand
    };

    private void OpenPixelMailCloudDialog(MailAccountConfig account)
    {
        var dlg = new Window
        {
            Owner = this,
            Title = "☁️ Резервное копирование и восстановление PixelMail",
            Width = 530,
            Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252))
        };

        var root = new StackPanel { Margin = new Thickness(20) };

        root.Children.Add(new TextBlock
        {
            Text = "Резервная копия почты PixelMail",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
        });

        root.Children.Add(new TextBlock
        {
            Text = $"Почтовый ящик: {account.EmailAddress}",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            Margin = new Thickness(0, 4, 0, 10)
        });

        root.Children.Add(new TextBlock
        {
            Text = "Задайте пароль для шифрования архива (при загрузке на Pixel) или введите ранее установленный пароль для восстановления (при сгрузке с Pixel).\nВсе письма шифруются алгоритмом AES-256-GCM перед передачей на сервер.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            Margin = new Thickness(0, 0, 0, 12)
        });

        root.Children.Add(new TextBlock
        {
            Text = "Пароль архива резервной копии:",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });

        var pwdBox = new PasswordBox
        {
            Height = 32,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 14)
        };
        root.Children.Add(pwdBox);

        var statusBlock = new TextBlock
        {
            Text = "Готов к синхронизации с сервером помощника",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            Margin = new Thickness(0, 0, 0, 14)
        };
        root.Children.Add(statusBlock);

        var actions = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 0) };

        var uploadBtn = ActionButton("☁️ Загрузить на Pixel", true);
        uploadBtn.ToolTip = "Создать зашифрованный архив всех писем и отправить на корпоративный сервер Pixel";
        uploadBtn.Margin = new Thickness(0, 0, 6, 0);

        var downloadBtn = ActionButton("📥 Сгрузить с Pixel", false);
        downloadBtn.ToolTip = "Скачать архив с сервера Pixel, проверить пароль и восстановить все письма в клиент";
        downloadBtn.Margin = new Thickness(6, 0, 0, 0);

        uploadBtn.Click += async (_, _) =>
        {
            string pass = pwdBox.Password.Trim();
            if (string.IsNullOrWhiteSpace(pass))
            {
                MessageBox.Show(dlg, "Введите пароль для шифрования резервной копии.", "Требуется пароль", MessageBoxButton.OK, MessageBoxImage.Warning);
                pwdBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(settings.ServerUrl))
            {
                MessageBox.Show(dlg, "Сервер помощника не настроен в конфигурации.", "Ошибка сервера", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            uploadBtn.IsEnabled = false;
            downloadBtn.IsEnabled = false;
            statusBlock.Text = "⏳ Создание и шифрование архива писем (AES-256-GCM)…";

            try
            {
                byte[] cipherPackage = await MailCryptoService.CreateEncryptedMailBackupAsync(
                    account.Id,
                    account.EmailAddress,
                    pass,
                    mailStorage);

                statusBlock.Text = "⏳ Отправка зашифрованного архива на сервер помощника…";

                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
                http.DefaultRequestHeaders.Add("X-Machine-Name", Environment.MachineName);
                if (!string.IsNullOrWhiteSpace(settings.ClientToken))
                    http.DefaultRequestHeaders.Add("X-Client-Key", settings.ClientToken);

                string uploadUrl = settings.ServerUrl.TrimEnd('/') + "/api/mail-backup/upload";
                var req = new
                {
                    machineName = Environment.MachineName,
                    accountEmail = account.EmailAddress,
                    payloadBase64 = Convert.ToBase64String(cipherPackage),
                    messageCount = mailStorage.GetAccountMessages(account.Id).Count
                };

                using var resp = await http.PostAsJsonAsync(uploadUrl, req);
                resp.EnsureSuccessStatusCode();

                statusBlock.Text = "✅ Резервная копия успешно сохранена на сервере Pixel!";
                MessageBox.Show(dlg,
                    $"Резервная копия ящика «{account.EmailAddress}» успешно зашифрована и сохранена на сервере Pixel!\n\nРазмер: {cipherPackage.Length / 1024:N0} КБ.\nПароль для восстановления: запомните указанный пароль.",
                    "Загружено на Pixel",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                dlg.Close();
            }
            catch (Exception ex)
            {
                statusBlock.Text = "❌ Ошибка: " + ex.Message;
                MessageBox.Show(dlg, "Ошибка при загрузке архива: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                uploadBtn.IsEnabled = true;
                downloadBtn.IsEnabled = true;
            }
        };

        downloadBtn.Click += async (_, _) =>
        {
            string pass = pwdBox.Password.Trim();
            if (string.IsNullOrWhiteSpace(pass))
            {
                MessageBox.Show(dlg, "Введите пароль для расшифровки архива.", "Требуется пароль", MessageBoxButton.OK, MessageBoxImage.Warning);
                pwdBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(settings.ServerUrl))
            {
                MessageBox.Show(dlg, "Сервер помощника не настроен в конфигурации.", "Ошибка сервера", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            uploadBtn.IsEnabled = false;
            downloadBtn.IsEnabled = false;
            statusBlock.Text = "⏳ Загрузка резервной копии с сервера помощника…";

            try
            {
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
                http.DefaultRequestHeaders.Add("X-Machine-Name", Environment.MachineName);
                if (!string.IsNullOrWhiteSpace(settings.ClientToken))
                    http.DefaultRequestHeaders.Add("X-Client-Key", settings.ClientToken);

                string downloadUrl = settings.ServerUrl.TrimEnd('/') + $"/api/mail-backup/download?accountEmail={Uri.EscapeDataString(account.EmailAddress)}";
                using var resp = await http.GetAsync(downloadUrl);
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    throw new InvalidOperationException("На сервере помощника нет сохранённой копии для этого почтового ящика.");
                }
                resp.EnsureSuccessStatusCode();

                byte[] cipherPackage = await resp.Content.ReadAsByteArrayAsync();
                statusBlock.Text = "⏳ Проверка пароля и расшифровка писем…";

                var (success, count, err) = await MailCryptoService.RestoreFromEncryptedMailBackupAsync(
                    cipherPackage,
                    pass,
                    account,
                    mailStorage);

                if (!success)
                {
                    statusBlock.Text = "❌ " + err;
                    MessageBox.Show(dlg, err, "Ошибка восстановления", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                statusBlock.Text = $"✅ Восстановлено писем: {count}";
                MessageBox.Show(dlg,
                    $"Восстановление почты успешно завершено!\n\nВосстановлено писем: {count}\nВсе письма добавлены в ваш почтовый клиент Pixel Mail.",
                    "Сгружено с Pixel",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                dlg.Close();
                SwitchTab("mail");
            }
            catch (Exception ex)
            {
                statusBlock.Text = "❌ Ошибка: " + ex.Message;
                MessageBox.Show(dlg, "Ошибка при восстановлении архива: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                uploadBtn.IsEnabled = true;
                downloadBtn.IsEnabled = true;
            }
        };

        actions.Children.Add(uploadBtn);
        actions.Children.Add(downloadBtn);
        root.Children.Add(actions);

        dlg.Content = root;
        dlg.ShowDialog();
    }
}
