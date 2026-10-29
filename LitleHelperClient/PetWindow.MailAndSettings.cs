using System.Windows;
using System.Windows.Controls;

namespace PixelHelper;

public sealed partial class PetWindow
{
    private MailStorageEngine mailStorage = null!;
    private MailBackgroundWorker? mailWorker;
    private MailWindow? mailWindow;
    private SettingsWindow? settingsWindow;

    internal MailStorageEngine MailStorage => mailStorage;

    private void InitializeMailSubsystem()
    {
        mailStorage = new MailStorageEngine(settings, antivirus);
        mailStorage.NewMessagesArrived += OnNewMailMessagesArrived;
        if (!diagnostics)
        {
            mailWorker = new MailBackgroundWorker(settings, mailStorage);
        }
    }

    private void StartMailSubsystem()
    {
        if (!diagnostics)
        {
            mailWorker?.Start();
        }
    }

    private void DisposeMailSubsystem()
    {
        try { mailWorker?.Dispose(); } catch { }
        try { mailWindow?.Close(); } catch { }
        try { settingsWindow?.Close(); } catch { }
    }

    /// <summary>
    /// Builds the unified right-click context menu for both the assistant sprite and the tray icon.
    /// Contains ONLY the items requested:
    /// - Статус помощника
    /// - Статус мессенджера
    /// - Статус последней связи с сервером
    /// - Мессенджер
    /// - Повторить подключение
    /// - Показать/Скрыть помощника
    /// - Не беспокоить
    /// - Настройки
    /// - Выход
    /// </summary>
    internal ContextMenu BuildAssistantContextMenu(UIElement? placementTarget = null)
    {
        var menu = new ContextMenu
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
        };
        if (placementTarget != null)
        {
            menu.PlacementTarget = placementTarget;
        }

        // 1. Статус помощника
        menu.Items.Add(new MenuItem
        {
            Header = Loc.Format("AssistantStatusLabel", (hub?.IsOnline == true ? "Online" : "Offline") + " · " + (hub?.Status ?? "—")),
            IsEnabled = false
        });

        // 2. Статус мессенджера
        menu.Items.Add(new MenuItem
        {
            Header = Loc.Format("MessengerStatusLabel", messenger?.ConnectionStatus ?? "—"),
            IsEnabled = false
        });

        // 3. Статус последней связи с сервером
        string lastSyncText = hub?.LastSuccess is DateTime success
            ? success.ToLocalTime().ToString("HH:mm:ss")
            : "—";
        menu.Items.Add(new MenuItem
        {
            Header = Loc.Format("LastServerSyncLabel", lastSyncText),
            IsEnabled = false
        });

        menu.Items.Add(new Separator());

        // 4. Мессенджер
        var chatItem = new MenuItem { Header = Loc.T("TrayMessenger") };
        chatItem.Click += async (_, _) => await OpenMessengerAsync();
        menu.Items.Add(chatItem);

        // 5. Повторить подключение
        var reconnectItem = new MenuItem
        {
            Header = Loc.T("TrayReconnect"),
            IsEnabled = !reconnecting
        };
        reconnectItem.Click += async (_, _) => await ReconnectAsync();
        menu.Items.Add(reconnectItem);

        // 6. Показать/Скрыть помощника
        var toggleAssistantItem = new MenuItem
        {
            Header = settings.AssistantHidden
                ? ("👁️ " + Loc.T("DisplayShow"))
                : ("🙈 " + Loc.T("DisplayHide"))
        };
        toggleAssistantItem.Click += (_, _) => SetAssistantHidden(!settings.AssistantHidden);
        menu.Items.Add(toggleAssistantItem);

        // 7. Не беспокоить
        var quietItem = new MenuItem
        {
            Header = Loc.T("TrayDoNotDisturb"),
            IsCheckable = true,
            IsChecked = settings.ChatDoNotDisturb
        };
        quietItem.Click += (_, _) =>
        {
            settings.ChatDoNotDisturb = quietItem.IsChecked;
            if (!diagnostics) settings.Save();
        };
        menu.Items.Add(quietItem);

        if (!diagnostics && (adminAvailable || superAvailable))
        {
            var ticketsItem = new MenuItem { Header = "📋 Заявки" };
            ticketsItem.Click += (_, _) => OpenStaffTicketsWindow();
            menu.Items.Add(ticketsItem);
        }

        menu.Items.Add(new Separator());

        // 8. Кнопка настроек
        var settingsItem = new MenuItem { Header = "⚙️ " + Loc.T("TraySettings") };
        settingsItem.Click += (_, _) => OpenSettingsWindow();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new Separator());

        // 9. Выход
        var exitItem = new MenuItem { Header = Loc.T("TrayExit") };
        exitItem.Click += (_, _) => Close();
        menu.Items.Add(exitItem);

        return menu;
    }

    internal void OpenSettingsWindow(string initialTab = "theme")
    {
        if (settingsWindow != null && settingsWindow.IsVisible)
        {
            settingsWindow.SwitchTab(initialTab);
            if (settingsWindow.WindowState == WindowState.Minimized)
                settingsWindow.WindowState = WindowState.Normal;
            settingsWindow.Activate();
            return;
        }

        settingsWindow = new SettingsWindow(settings, this, antivirus, mailStorage, initialTab);
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show();
        settingsWindow.Activate();
    }

    internal void OpenMailWindow(MailMessageItem? selectMessage = null)
    {
        if (!SystemButtonVisibility.Enabled(systemButtons, "mail"))
        {
            ShowNotice("Функция почтового клиента временно отключена администратором через веб-панель управления.");
            return;
        }

        if (mailWindow == null || !mailWindow.IsLoaded)
        {
            mailWindow = new MailWindow(
                settings,
                mailStorage,
                antivirus,
                () => OpenSettingsWindow("mail"));
            mailWindow.Closed += (_, _) => mailWindow = null;
            mailWindow.Show();
        }

        if (mailWindow.WindowState == WindowState.Minimized)
            mailWindow.WindowState = WindowState.Normal;
        mailWindow.Activate();

        if (selectMessage != null)
        {
            mailWindow.SelectAndOpenMessage(selectMessage);
        }
        else
        {
            mailWindow.RefreshMailboxesAndMessages();
        }
    }

    internal void OpenMailFile(string filePath)
    {
        if (!SystemButtonVisibility.Enabled(systemButtons, "mail"))
        {
            ShowNotice("Функция почтового клиента временно отключена администратором через веб-панель управления.");
            return;
        }
        OpenMailWindow();
        mailWindow?.OpenMailFile(filePath);
    }

    private void OnNewMailMessagesArrived(List<MailMessageItem> newMessages)
    {
        if (newMessages.Count == 0) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                mailWindow?.RefreshMailboxesAndMessages();

                var inboxMessages = newMessages.Where(m => m.Folder is MailFolderKind.Inbox or MailFolderKind.LocalArchive).ToList();
                var quarantined = newMessages.Where(m => m.SecurityVerdict == MailSecurityVerdict.BlockedMalware).ToList();

                if (quarantined.Count > 0)
                {
                    var qMsg = quarantined[0];
                    string qNotice = $"🛡️ PixelGuard AV изолировал опасное письмо от {qMsg.FromAddress}:\n«{qMsg.Subject}»\n{qMsg.SecurityReason}";
                    if (!settings.AssistantHidden && IsVisible)
                    {
                        React(PetState.Offended, 5);
                        if (announcements.Count < 10)
                            ReceiveAnnouncement(new ClientNotice(qNotice, "PixelGuard AV · Почта", 20) { LocalAnimation = PetState.Offended });
                    }
                    else if (settings.MailNotifyWhenHidden)
                    {
                        var toast = new MailNotificationToastWindow(
                            qMsg,
                            settings.ChatPreview,
                            () => OpenMailWindow(qMsg),
                            () => mailStorage.MarkAsRead(qMsg.Id, true));
                        toast.Show();
                    }
                }

                if (inboxMessages.Count == 0) return;
                if (settings.ChatDoNotDisturb) return;

                var latest = inboxMessages[0];
                bool soundProfileActive = SoundManager.NormalizeProfile(settings.SoundProfile) != "Mute";
                if (settings.ChatSound && soundProfileActive)
                {
                    SoundManager.Play(SoundEvent.MessageReceived, settings);
                }

                tray?.SetUnread(true);

                // Always show toast notification when assistant is hidden (or MailNotifyWhenHidden is enabled)
                if (settings.AssistantHidden || !IsVisible || settings.MailNotifyWhenHidden)
                {
                    var toast = new MailNotificationToastWindow(
                        latest,
                        settings.ChatPreview,
                        () => OpenMailWindow(latest),
                        () => mailStorage.MarkAsRead(latest.Id, true));
                    toast.Show();
                }

                if (!settings.AssistantHidden && IsVisible)
                {
                    InterruptCurrentAnimationForMessage(PetState.Joy, 4);
                    ShowFaceMail(5);
                    if (settings.ChatComicNotifications && announcements.Count < 10)
                    {
                        string text = inboxMessages.Count == 1
                            ? $"Новое письмо ({latest.AccountEmail}):\nОт: {latest.SenderDisplay}\nТема: {latest.Subject}"
                            : $"Новых писем ({inboxMessages.Count}) на {latest.AccountEmail}\nПоследнее: {latest.Subject}";
                        ReceiveAnnouncement(new ClientNotice(text, "Pixel Mail", 15) { LocalAnimation = PetState.Joy });
                    }
                }
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
            }
        }));
    }
}

