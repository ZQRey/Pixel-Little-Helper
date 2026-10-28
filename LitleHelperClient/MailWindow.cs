using Microsoft.Win32;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelHelper;

internal sealed class MailWindow : Window
{
    private readonly Settings settings;
    private readonly MailStorageEngine storage;
    private readonly AntivirusService? antivirus;
    private readonly Action openMailSettings;

    private string? selectedAccountId;
    private MailFolderKind selectedFolder = MailFolderKind.UnifiedInbox;
    private string selectedFilter = "all";
    private MailMessageItem? selectedMessage;
    private bool suppressAccountSelectionChanged;
    private bool suppressMessageSelectionChanged;
    private List<MailMessageItem>? importedMessages;
    private string? importedFileName;

    private readonly ComboBox accountFilterBox;
    private readonly StackPanel folderListPanel;
    private readonly TextBox searchBox;
    private readonly StackPanel filterPillsPanel;
    private readonly ListBox messageListBox;
    private readonly StackPanel readingPaneContent;
    private readonly TextBlock statusFooter;
    private readonly Button composeBtn;
    private readonly Button syncBtn;
    private readonly Button openFileBtn;
    private readonly Button replyBtn;
    private readonly Button forwardBtn;
    private readonly Button settingsBtn;
    private readonly TextBlock mailboxesHeaderBlock;
    private readonly TextBlock foldersHeaderBlock;
    private readonly Button addMailboxQuickBtn;

    internal MailWindow(
        Settings settings,
        MailStorageEngine storage,
        AntivirusService? antivirus,
        Action openMailSettings)
    {
        this.settings = settings;
        this.storage = storage;
        this.antivirus = antivirus;
        this.openMailSettings = openMailSettings;

        Title = Loc.T("MailWindowTitle");
        Width = 1080;
        Height = 680;
        MinWidth = 840;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
        AllowDrop = true;
        DragOver += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    string target = files[0];
                    string ext = Path.GetExtension(target).ToLowerInvariant();
                    if (ext is ".eml" or ".mbox")
                    {
                        OpenMailFile(target);
                    }
                }
            }
        };

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 1. Top Toolbar
        var topBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 10, 14, 10)
        };
        var topDock = new DockPanel();

        var rightBar = new StackPanel { Orientation = Orientation.Horizontal };
        searchBox = new TextBox
        {
            Width = 210,
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Loc.T("MailSearchTooltip")
        };
        searchBox.TextChanged += (_, _) => RefreshMessageList();
        rightBar.Children.Add(searchBox);

        settingsBtn = CreateToolbarButton(Loc.T("MailSettingsBtn"), isPrimary: false);
        settingsBtn.Click += (_, _) => openMailSettings();
        rightBar.Children.Add(settingsBtn);

        DockPanel.SetDock(rightBar, Dock.Right);
        topDock.Children.Add(rightBar);

        var leftActions = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        composeBtn = CreateToolbarButton(Loc.T("MailComposeBtn"), isPrimary: true);
        composeBtn.Click += (_, _) => OpenCompose();
        leftActions.Children.Add(composeBtn);

        syncBtn = CreateToolbarButton(Loc.T("MailSyncBtn"), isPrimary: false);
        syncBtn.Click += async (_, _) => await SyncNowAsync();
        leftActions.Children.Add(syncBtn);

        openFileBtn = CreateToolbarButton(Loc.T("MailOpenFileBtn"), isPrimary: false);
        openFileBtn.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog
            {
                Title = "Открыть письмо или архив писем",
                Filter = "Почтовые файлы (*.eml;*.mbox)|*.eml;*.mbox|Письма EML (*.eml)|*.eml|Архивы MBOX (*.mbox)|*.mbox|Все файлы (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) == true)
            {
                OpenMailFile(dlg.FileName);
            }
        };
        leftActions.Children.Add(openFileBtn);

        replyBtn = CreateToolbarButton(Loc.T("MailReplyBtn"), isPrimary: false);
        replyBtn.Click += (_, _) => ReplySelected();
        leftActions.Children.Add(replyBtn);

        forwardBtn = CreateToolbarButton(Loc.T("MailForwardBtn"), isPrimary: false);
        forwardBtn.Click += (_, _) => ForwardSelected();
        leftActions.Children.Add(forwardBtn);

        topDock.Children.Add(leftActions);
        topBorder.Child = topDock;
        Grid.SetRow(topBorder, 0);
        rootGrid.Children.Add(topBorder);

        // 2. Main 3-Pane Body
        var bodyGrid = new Grid();
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(235) });
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left Pane: Accounts + Folders
        var leftBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            Padding = new Thickness(12)
        };
        var leftStack = new StackPanel();
        mailboxesHeaderBlock = new TextBlock
        {
            Text = Loc.T("MailboxesHeader"),
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(4, 0, 0, 6)
        };
        leftStack.Children.Add(mailboxesHeaderBlock);

        accountFilterBox = new ComboBox
        {
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 0, 14)
        };
        accountFilterBox.SelectionChanged += (_, _) =>
        {
            if (suppressAccountSelectionChanged) return;
            if (accountFilterBox.SelectedItem is ComboBoxItem cbi)
            {
                selectedAccountId = cbi.Tag as string;
                RefreshFolders();
                RefreshMessageList();
            }
        };
        leftStack.Children.Add(accountFilterBox);

        foldersHeaderBlock = new TextBlock
        {
            Text = Loc.T("FoldersHeader"),
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(4, 0, 0, 6)
        };
        leftStack.Children.Add(foldersHeaderBlock);

        folderListPanel = new StackPanel();
        leftStack.Children.Add(folderListPanel);

        addMailboxQuickBtn = new Button
        {
            Content = Loc.T("ConnectMailboxQuick"),
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Foreground = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        addMailboxQuickBtn.Click += (_, _) => openMailSettings();
        leftStack.Children.Add(addMailboxQuickBtn);

        leftBorder.Child = leftStack;
        Grid.SetColumn(leftBorder, 0);
        bodyGrid.Children.Add(leftBorder);

        // Middle Pane: Filter pills + Message List
        var midBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        var midGrid = new Grid();
        midGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        midGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        filterPillsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 8, 8, 6)
        };
        Grid.SetRow(filterPillsPanel, 0);
        midGrid.Children.Add(filterPillsPanel);

        messageListBox = new ListBox
        {
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(messageListBox, ScrollBarVisibility.Disabled);
        messageListBox.SelectionChanged += (_, _) =>
        {
            if (suppressMessageSelectionChanged) return;
            if (messageListBox.SelectedItem is ListBoxItem lbi && lbi.Tag is MailMessageItem msg)
            {
                SelectMessage(msg);
            }
        };
        Grid.SetRow(messageListBox, 1);
        midGrid.Children.Add(messageListBox);
        midBorder.Child = midGrid;
        Grid.SetColumn(midBorder, 1);
        bodyGrid.Children.Add(midBorder);

        // Right Pane: Reading Pane
        var rightBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            Padding = new Thickness(18)
        };
        readingPaneContent = new StackPanel();
        rightBorder.Child = new ScrollViewer
        {
            Content = readingPaneContent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetColumn(rightBorder, 2);
        bodyGrid.Children.Add(rightBorder);

        Grid.SetRow(bodyGrid, 1);
        rootGrid.Children.Add(bodyGrid);

        // 3. Status Bar
        var footerBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(14, 6, 14, 6)
        };
        statusFooter = new TextBlock
        {
            Text = "🛡️ PixelGuard AV Mail Shield активен · Проверка входящих писем, вложений, ссылок и спама включена",
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            FontSize = 12
        };
        footerBorder.Child = statusFooter;
        Grid.SetRow(footerBorder, 2);
        rootGrid.Children.Add(footerBorder);

        Content = rootGrid;

        storage.MessagesUpdated += OnStorageUpdated;
        Loc.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) =>
        {
            storage.MessagesUpdated -= OnStorageUpdated;
            Loc.LanguageChanged -= OnLanguageChanged;
        };

        RefreshAccountsDropdown();
        RefreshFilterPills();
        RefreshFolders();
        RefreshMessageList();
        RenderReadingPane(null);
    }

    private void OnLanguageChanged()
    {
        Dispatcher.InvokeAsync(() =>
        {
            Title = Loc.T("MailWindowTitle");
            composeBtn.Content = Loc.T("MailComposeBtn");
            syncBtn.Content = Loc.T("MailSyncBtn");
            openFileBtn.Content = Loc.T("MailOpenFileBtn");
            replyBtn.Content = Loc.T("MailReplyBtn");
            forwardBtn.Content = Loc.T("MailForwardBtn");
            settingsBtn.Content = Loc.T("MailSettingsBtn");
            searchBox.ToolTip = Loc.T("MailSearchTooltip");
            mailboxesHeaderBlock.Text = Loc.T("MailboxesHeader");
            foldersHeaderBlock.Text = Loc.T("FoldersHeader");
            addMailboxQuickBtn.Content = Loc.T("ConnectMailboxQuick");
            statusFooter.Text = Loc.T("MailShieldActive");
            RefreshAccountsDropdown();
            RefreshFilterPills();
            RefreshFolders();
            RefreshMessageList();
        });
    }

    private void OnStorageUpdated()
    {
        Dispatcher.InvokeAsync(() =>
        {
            RefreshAccountsDropdown();
            RefreshFolders();
            RefreshMessageList();
            var lastSync = settings.MailAccounts
                .Where(a => a.LastSyncUtc.HasValue)
                .OrderByDescending(a => a.LastSyncUtc)
                .FirstOrDefault();
            if (lastSync?.LastSyncUtc is DateTime dt)
            {
                statusFooter.Text = $"🛡️ PixelGuard AV Mail Shield · Автосинхронизация ({dt.ToLocalTime():HH:mm:ss}): {lastSync.LastSyncStatus}";
            }
        });
    }

    private static Button CreateToolbarButton(string text, bool isPrimary)
    {
        return new Button
        {
            Content = text,
            Padding = new Thickness(11, 5, 11, 5),
            Margin = new Thickness(0, 0, 6, 0),
            Background = isPrimary ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : new SolidColorBrush(Color.FromRgb(241, 245, 249)),
            Foreground = isPrimary ? Brushes.White : new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            BorderBrush = new SolidColorBrush(isPrimary ? Color.FromRgb(29, 78, 216) : Color.FromRgb(203, 213, 225)),
            BorderThickness = new Thickness(1),
            FontWeight = isPrimary ? FontWeights.SemiBold : FontWeights.Normal,
            Cursor = Cursors.Hand
        };
    }

    internal void RefreshAccountsDropdown()
    {
        string? prevId = selectedAccountId;
        suppressAccountSelectionChanged = true;
        try
        {
            accountFilterBox.Items.Clear();
            accountFilterBox.Items.Add(new ComboBoxItem
            {
                Content = $"{Loc.T("AllMailboxes")} ({settings.MailAccounts.Count})",
                Tag = null
            });

            int selIndex = 0;
            for (int i = 0; i < settings.MailAccounts.Count; i++)
            {
                var acc = settings.MailAccounts[i];
                string modeBadge = acc.StorageMode == MailStorageMode.DownloadAllToComputer ? "💻 ПК" : "☁️ Сервер";
                accountFilterBox.Items.Add(new ComboBoxItem
                {
                    Content = $"{acc.EmailAddress} ({modeBadge})",
                    Tag = acc.Id
                });
                if (prevId != null && acc.Id == prevId) selIndex = i + 1;
            }
            accountFilterBox.SelectedIndex = selIndex;
        }
        finally
        {
            suppressAccountSelectionChanged = false;
        }
    }

    private void RefreshFilterPills()
    {
        filterPillsPanel.Children.Clear();
        foreach (var (key, label) in new[]
        {
            ("all", Loc.T("FilterAll")),
            ("unread", Loc.T("FilterUnread")),
            ("attachments", Loc.T("FilterAttachments")),
            ("encrypted", Loc.T("FilterEncrypted")),
            ("threats", Loc.T("FilterThreats"))
        })
        {
            bool active = selectedFilter == key;
            var btn = new Button
            {
                Content = label,
                Padding = new Thickness(7, 3, 7, 3),
                Margin = new Thickness(0, 0, 4, 0),
                FontSize = 11,
                Background = active ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            string k = key;
            btn.Click += (_, _) =>
            {
                selectedFilter = k;
                RefreshFilterPills();
                RefreshMessageList();
            };
            filterPillsPanel.Children.Add(btn);
        }
    }

    private void RefreshFolders()
    {
        folderListPanel.Children.Clear();
        var folders = new (MailFolderKind Kind, string Title)[]
        {
            (MailFolderKind.UnifiedInbox, Loc.T("FolderUnifiedInbox")),
            (MailFolderKind.Inbox, Loc.T("FolderInbox")),
            (MailFolderKind.Sent, Loc.T("FolderSent")),
            (MailFolderKind.Drafts, Loc.T("FolderDrafts")),
            (MailFolderKind.Spam, Loc.T("FolderSpam")),
            (MailFolderKind.Trash, Loc.T("FolderTrash")),
            (MailFolderKind.LocalArchive, Loc.T("FolderLocalArchive"))
        };

        foreach (var (kind, title) in folders)
        {
            bool isSelected = selectedFolder == kind;
            int unread = storage.GetUnreadCountForAccount(selectedAccountId ?? "", kind);
            string display;
            if (kind == MailFolderKind.LocalArchive && importedMessages != null && importedMessages.Count > 0)
            {
                string name = !string.IsNullOrWhiteSpace(importedFileName) ? importedFileName : "Файл";
                display = $"📂 {name} ({importedMessages.Count})";
            }
            else
            {
                display = unread > 0 ? $"{title} ({unread})" : title;
            }

            var btn = new Button
            {
                Content = display,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 3),
                Background = isSelected ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : Brushes.Transparent,
                Foreground = isSelected ? Brushes.White : new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(0),
                FontWeight = isSelected || unread > 0 ? FontWeights.SemiBold : FontWeights.Normal,
                Cursor = Cursors.Hand
            };
            var k = kind;
            btn.Click += (_, _) =>
            {
                selectedFolder = k;
                RefreshFolders();
                RefreshMessageList();
            };
            folderListPanel.Children.Add(btn);
        }
    }

    private void RefreshMessageList()
    {
        List<MailMessageItem> items;
        if (selectedFolder == MailFolderKind.LocalArchive && importedMessages != null && importedMessages.Count > 0)
        {
            string q = searchBox.Text.Trim().ToLowerInvariant();
            items = importedMessages
                .Where(m => string.IsNullOrWhiteSpace(q) ||
                            (m.Subject?.ToLowerInvariant().Contains(q) == true) ||
                            (m.BodyText?.ToLowerInvariant().Contains(q) == true) ||
                            (m.SenderDisplay?.ToLowerInvariant().Contains(q) == true))
                .OrderByDescending(m => m.DateUtc)
                .ToList();
        }
        else
        {
            items = storage.QueryMessages(selectedAccountId, selectedFolder, searchBox.Text, selectedFilter);
        }
        suppressMessageSelectionChanged = true;
        try
        {
            messageListBox.Items.Clear();

            if (items.Count == 0)
            {
                messageListBox.Items.Add(new ListBoxItem
                {
                    IsEnabled = false,
                    Content = new TextBlock
                    {
                        Text = settings.MailAccounts.Count == 0
                            ? "Почтовые ящики ещё не подключены.\nНажмите «⚙️ Настройки почты», чтобы добавить 1 или несколько ящиков."
                            : "В этой папке пока нет писем.",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                        Margin = new Thickness(12, 24, 12, 24),
                        TextAlignment = TextAlignment.Center
                    }
                });
                return;
            }

            foreach (var msg in items)
            {
                var card = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(10, 8, 10, 8),
                    Background = msg.IsRead ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(239, 246, 255))
                };

                var stack = new StackPanel();
                var topRow = new DockPanel();
                topRow.Children.Add(new TextBlock
                {
                    Text = msg.DateUtc.ToLocalTime().ToString("dd.MM HH:mm"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    Margin = new Thickness(6, 0, 0, 0)
                });
                DockPanel.SetDock(topRow.Children[0], Dock.Right);

                string secIcon = msg.SecurityVerdict switch
                {
                    MailSecurityVerdict.BlockedMalware => "🚨 ",
                    MailSecurityVerdict.BlockedBlacklist => "🚫 ",
                    MailSecurityVerdict.PhishingWarning => "⚠️ ",
                    MailSecurityVerdict.Spam => "🛡️ ",
                    MailSecurityVerdict.TrustedWhitelist => "✅ ",
                    _ => "🛡️ "
                };
                string encIcon = msg.IsEncrypted ? "🔒 " : "";
                string attIcon = msg.HasAttachments ? "📎 " : "";

                topRow.Children.Add(new TextBlock
                {
                    Text = $"{secIcon}{encIcon}{attIcon}{(string.IsNullOrWhiteSpace(msg.FromName) ? msg.FromAddress : msg.FromName)}",
                    FontWeight = msg.IsRead ? FontWeights.Medium : FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                stack.Children.Add(topRow);

                stack.Children.Add(new TextBlock
                {
                    Text = msg.Subject,
                    FontWeight = msg.IsRead ? FontWeights.Normal : FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 2, 0, 2)
                });

                stack.Children.Add(new TextBlock
                {
                    Text = $"[{msg.AccountEmail}] {msg.Snippet}",
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                card.Child = stack;
                var capturedMsg = msg;
                var lbi = new ListBoxItem { Content = card, Tag = msg, Padding = new Thickness(0) };
                lbi.ContextMenu = BuildMessageContextMenu(capturedMsg);
                lbi.PreviewMouseRightButtonDown += (_, _) =>
                {
                    lbi.IsSelected = true;
                    selectedMessage = capturedMsg;
                };
                messageListBox.Items.Add(lbi);

                if (selectedMessage != null && selectedMessage.Id == msg.Id)
                {
                    messageListBox.SelectedItem = lbi;
                }
            }
        }
        finally
        {
            suppressMessageSelectionChanged = false;
        }
    }

    private ContextMenu BuildMessageContextMenu(MailMessageItem msg)
    {
        var menu = new ContextMenu();

        var toggleReadItem = new MenuItem
        {
            Header = msg.IsRead ? Loc.T("MailContextMarkUnread") : Loc.T("MailContextMarkRead")
        };
        toggleReadItem.Click += (_, _) =>
        {
            bool newReadState = !msg.IsRead;
            msg.IsRead = newReadState;
            storage.MarkAsRead(msg.Id, newReadState);
            if (!newReadState && selectedMessage?.Id == msg.Id)
            {
                selectedMessage = null;
                messageListBox.SelectedItem = null;
                RenderReadingPane(null);
            }
            RefreshFolders();
            RefreshMessageList();
        };
        menu.Items.Add(toggleReadItem);

        var spamItem = new MenuItem
        {
            Header = Loc.T("MailContextSpam")
        };
        spamItem.Click += (_, _) =>
        {
            storage.MoveToFolder(msg.Id, MailFolderKind.Spam);
            if (selectedMessage?.Id == msg.Id)
            {
                selectedMessage = null;
                RenderReadingPane(null);
            }
            RefreshFolders();
            RefreshMessageList();
        };
        menu.Items.Add(spamItem);

        menu.Items.Add(new Separator());

        var deleteItem = new MenuItem
        {
            Header = Loc.T("MailContextDelete")
        };
        deleteItem.Click += (_, _) =>
        {
            storage.DeleteMessage(msg.Id);
            if (selectedMessage?.Id == msg.Id)
            {
                selectedMessage = null;
                RenderReadingPane(null);
            }
            RefreshFolders();
            RefreshMessageList();
        };
        menu.Items.Add(deleteItem);

        return menu;
    }

    internal void SelectMessage(MailMessageItem msg)
    {
        selectedMessage = msg;
        if (!msg.IsRead)
        {
            msg.IsRead = true;
            storage.MarkAsRead(msg.Id, true);
            RefreshFolders();
        }
        RenderReadingPane(msg);
    }

    private void RenderReadingPane(MailMessageItem? msg)
    {
        readingPaneContent.Children.Clear();
        if (msg == null)
        {
            readingPaneContent.Children.Add(new TextBlock
            {
                Text = "Выберите письмо в списке слева для чтения и проверки безопасности.",
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                FontSize = 14,
                Margin = new Thickness(0, 40, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }

        // Subject
        readingPaneContent.Children.Add(new TextBlock
        {
            Text = msg.Subject,
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        // Security Banner (PixelGuard AV)
        var (bannerBg, bannerBorder, bannerFg) = msg.SecurityVerdict switch
        {
            MailSecurityVerdict.BlockedMalware or MailSecurityVerdict.BlockedBlacklist =>
                (Color.FromRgb(254, 226, 226), Color.FromRgb(239, 68, 68), Color.FromRgb(153, 27, 27)),
            MailSecurityVerdict.PhishingWarning or MailSecurityVerdict.Spam =>
                (Color.FromRgb(254, 243, 199), Color.FromRgb(245, 158, 11), Color.FromRgb(146, 64, 14)),
            _ =>
                (Color.FromRgb(220, 252, 231), Color.FromRgb(34, 197, 94), Color.FromRgb(22, 101, 52))
        };

        var secBanner = new Border
        {
            Background = new SolidColorBrush(bannerBg),
            BorderBrush = new SolidColorBrush(bannerBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var secStack = new StackPanel();
        secStack.Children.Add(new TextBlock
        {
            Text = msg.SecurityReason,
            Foreground = new SolidColorBrush(bannerFg),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        foreach (string warn in msg.PhishingWarnings)
        {
            secStack.Children.Add(new TextBlock
            {
                Text = "• " + warn,
                Foreground = new SolidColorBrush(bannerFg),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 2, 0, 0)
            });
        }
        secBanner.Child = secStack;
        readingPaneContent.Children.Add(secBanner);

        // Sender & Meta Card
        var metaCard = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var metaStack = new StackPanel();
        metaStack.Children.Add(new TextBlock
        {
            Text = $"От: {msg.SenderDisplay}",
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
        });
        metaStack.Children.Add(new TextBlock
        {
            Text = $"Кому: {(msg.ToAddresses.Count > 0 ? string.Join(", ", msg.ToAddresses) : msg.AccountEmail)} · Дата: {msg.DateUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            Margin = new Thickness(0, 2, 0, 6)
        });

        string storageBadge = msg.DownloadedToPc
            ? "💻 Выгружено и зашифровано на ПК (AES-256-GCM)"
            : "☁️ Синхронизировано с основным сервером";
        if (msg.IsEncrypted)
        {
            storageBadge += $" · 🔒 Сквозное шифрование ({msg.EncryptionType})";
        }
        metaStack.Children.Add(new TextBlock
        {
            Text = storageBadge,
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            Margin = new Thickness(0, 0, 0, 8)
        });

        // Quick Blacklist / Whitelist / Decrypt buttons
        var quickRow = new WrapPanel();
        if (!string.IsNullOrWhiteSpace(msg.FromAddress))
        {
            var blockSenderBtn = CreateToolbarButton($"🚫 Заблокировать {msg.FromAddress}", false);
            blockSenderBtn.FontSize = 11.5;
            blockSenderBtn.Click += (_, _) =>
            {
                MailSecurityScanner.AddToBlacklist(msg.FromAddress, settings);
                storage.ApplyFilterListsToExistingMessages();
                statusFooter.Text = $"🚫 Адрес {msg.FromAddress} добавлен в чёрный список PixelGuard AV.";
                RenderReadingPane(msg);
            };
            quickRow.Children.Add(blockSenderBtn);

            if (msg.FromAddress.Contains('@'))
            {
                string dom = "@" + msg.FromAddress.Split('@').Last();
                var blockDomainBtn = CreateToolbarButton($"🚫 Блок домена {dom}", false);
                blockDomainBtn.FontSize = 11.5;
                blockDomainBtn.Click += (_, _) =>
                {
                    MailSecurityScanner.AddToBlacklist(dom, settings);
                    storage.ApplyFilterListsToExistingMessages();
                    statusFooter.Text = $"🚫 Домен {dom} добавлен в чёрный список PixelGuard AV.";
                    RenderReadingPane(msg);
                };
                quickRow.Children.Add(blockDomainBtn);
            }

            var trustSenderBtn = CreateToolbarButton($"✅ В белый список ({msg.FromAddress})", false);
            trustSenderBtn.FontSize = 11.5;
            trustSenderBtn.Click += (_, _) =>
            {
                MailSecurityScanner.AddToWhitelist(msg.FromAddress, settings);
                storage.ApplyFilterListsToExistingMessages();
                statusFooter.Text = $"✅ Адрес {msg.FromAddress} добавлен в белый список доверенных отправителей.";
                RenderReadingPane(msg);
            };
            quickRow.Children.Add(trustSenderBtn);
        }

        if (MailCryptoService.IsPixelAesEncrypted(msg.BodyText))
        {
            var decryptBtn = CreateToolbarButton("🔓 Расшифровать по паролю…", true);
            decryptBtn.FontSize = 11.5;
            decryptBtn.Click += (_, _) => PromptDecryptMessage(msg);
            quickRow.Children.Add(decryptBtn);
        }

        metaStack.Children.Add(quickRow);
        metaCard.Child = metaStack;
        readingPaneContent.Children.Add(metaCard);

        // Attachments
        if (msg.Attachments.Count > 0)
        {
            var attCard = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var attStack = new StackPanel();
            attStack.Children.Add(new TextBlock
            {
                Text = $"📎 Вложения ({msg.Attachments.Count})",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            foreach (var att in msg.Attachments)
            {
                var attRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                if (att.IsThreat)
                {
                    attRow.Children.Add(new TextBlock
                    {
                        Text = $"🚨 {att.FileName} ({att.SizeBytes / 1024.0:F1} КБ) — ЗАБЛОКИРОВАНО PixelGuard AV ({att.ThreatName}, изолировано в карантин)",
                        Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                        FontWeight = FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                else
                {
                    var saveAttBtn = CreateToolbarButton("💾 Сохранить", false);
                    saveAttBtn.FontSize = 11.5;
                    var capturedAtt = att;
                    saveAttBtn.Click += (_, _) => SaveAttachmentToDisk(capturedAtt);
                    DockPanel.SetDock(saveAttBtn, Dock.Right);
                    attRow.Children.Add(saveAttBtn);

                    attRow.Children.Add(new TextBlock
                    {
                        Text = $"🛡️ {att.FileName} ({att.SizeBytes / 1024.0:F1} КБ) — Проверено PixelGuard AV",
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
                    });
                }
                attStack.Children.Add(attRow);
            }

            attCard.Child = attStack;
            readingPaneContent.Children.Add(attCard);
        }

        // Body Card
        var bodyCard = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16)
        };
        var bodyStack = new StackPanel();
        var bodyTextBox = new TextBox
        {
            Text = msg.BodyText,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13.5,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
        };
        bodyStack.Children.Add(bodyTextBox);

        // Extract and display safe/checked links via WebGuard
        var linkMatches = Regex.Matches(msg.BodyText ?? "", @"https?://[^\s""'<>]+", RegexOptions.IgnoreCase);
        if (linkMatches.Count > 0)
        {
            bodyStack.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 8) });
            bodyStack.Children.Add(new TextBlock
            {
                Text = "🔗 Ссылки в письме (защита WebGuard):",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            foreach (Match m in linkMatches.Take(8))
            {
                string url = m.Value.TrimEnd('.', ',', ';', ')');
                var wg = WebGuard.Check(url, settings);
                var linkBtn = new Button
                {
                    Content = wg.Verdict == WebGuardVerdict.Safe
                        ? $"🟢 Открыть: {url}"
                        : wg.Verdict == WebGuardVerdict.Warning
                            ? $"⚠️ Подозрительная ссылка: {url} ({wg.Reason})"
                            : $"🚫 Заблокировано WebGuard: {url} ({wg.Reason})",
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 2, 0, 2),
                    Padding = new Thickness(8, 4, 8, 4),
                    FontSize = 11.5,
                    Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    Cursor = Cursors.Hand
                };
                string targetUrl = url;
                var verdict = wg.Verdict;
                string reason = wg.Reason;
                linkBtn.Click += (_, _) =>
                {
                    if (verdict == WebGuardVerdict.Blocked)
                    {
                        MessageBox.Show(this, $"Переход по фишинговой ссылке заблокирован модулем WebGuard:\n{targetUrl}\n\nПричина: {reason}", "WebGuard — Ссылка заблокирована", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    if (verdict == WebGuardVerdict.Warning)
                    {
                        if (MessageBox.Show(this, $"Ссылка выглядит подозрительно:\n{targetUrl}\n\nПричина: {reason}\n\nВсё равно открыть?", "Предупреждение WebGuard", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                            return;
                    }
                    WebGuard.LaunchUrl(targetUrl, settings);
                };
                bodyStack.Children.Add(linkBtn);
            }
        }

        bodyCard.Child = bodyStack;
        readingPaneContent.Children.Add(bodyCard);
    }

    private void SaveAttachmentToDisk(MailAttachmentItem att)
    {
        byte[]? bytes = storage.ReadAttachmentDecrypted(att);
        if (bytes == null)
        {
            MessageBox.Show(this, "Не удалось прочитать файл вложения или он изолирован антивирусом.", "Вложение недоступно", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new SaveFileDialog
        {
            FileName = att.FileName,
            Title = "Сохранить проверенное вложение"
        };
        if (dlg.ShowDialog(this) == true)
        {
            File.WriteAllBytes(dlg.FileName, bytes);
            statusFooter.Text = $"💾 Вложение «{att.FileName}» сохранено: {dlg.FileName}";
        }
    }

    private void PromptDecryptMessage(MailMessageItem msg)
    {
        var win = new Window
        {
            Title = "Расшифровка письма (Pixel-AES-256)",
            Width = 380,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };
        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(new TextBlock { Text = "Введите ключ/пароль шифрования письма:", Margin = new Thickness(0, 0, 0, 8) });
        var pwd = new PasswordBox { Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 12) };
        sp.Children.Add(pwd);
        var ok = CreateToolbarButton("Расшифровать", true);
        ok.Click += (_, _) =>
        {
            if (MailCryptoService.TryDecryptMessagePixelAes(msg.BodyText, pwd.Password, out string plain))
            {
                msg.BodyText = plain;
                win.DialogResult = true;
                win.Close();
                RenderReadingPane(msg);
            }
            else
            {
                MessageBox.Show(win, "Неверный ключ шифрования.", "Ошибка расшифровки", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        sp.Children.Add(ok);
        win.Content = sp;
        win.ShowDialog();
    }

    private void OpenCompose(string? to = null, string? subject = null, string? body = null)
    {
        var compose = new MailComposeWindow(settings, storage, antivirus, to, subject, body, selectedAccountId)
        {
            Owner = this
        };
        if (compose.ShowDialog() == true)
        {
            RefreshFolders();
            RefreshMessageList();
        }
    }

    private async Task SyncNowAsync()
    {
        if (settings.MailAccounts.Count == 0)
        {
            openMailSettings();
            return;
        }
        statusFooter.Text = "🔄 Синхронизация почтовых ящиков и проверка писем PixelGuard AV…";
        var arrived = await storage.SyncAllAccountsAsync();
        statusFooter.Text = $"✅ Проверка завершена ({DateTime.Now:HH:mm:ss}). Новых писем: {arrived.Count}";
        RefreshFolders();
        RefreshMessageList();
    }

    private void ReplySelected()
    {
        if (selectedMessage == null) return;
        string replyTo = !string.IsNullOrWhiteSpace(selectedMessage.ReplyToAddress) ? selectedMessage.ReplyToAddress : selectedMessage.FromAddress;
        string subj = selectedMessage.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
            ? selectedMessage.Subject
            : "Re: " + selectedMessage.Subject;
        string quoted = $"\n\n--- Исходное письмо от {selectedMessage.SenderDisplay} ({selectedMessage.DateUtc.ToLocalTime():dd.MM.yyyy HH:mm}) ---\n" + selectedMessage.BodyText;
        OpenCompose(replyTo, subj, quoted);
    }

    private void ForwardSelected()
    {
        if (selectedMessage == null) return;
        string subj = selectedMessage.Subject.StartsWith("Fwd:", StringComparison.OrdinalIgnoreCase)
            ? selectedMessage.Subject
            : "Fwd: " + selectedMessage.Subject;
        string quoted = $"\n\n--- Пересылаемое письмо от {selectedMessage.SenderDisplay} ---\n" + selectedMessage.BodyText;
        OpenCompose("", subj, quoted);
    }

    private void OffloadToPcDialog()
    {
        var acc = settings.MailAccounts.FirstOrDefault(a => a.Id == selectedAccountId) ?? settings.MailAccounts.FirstOrDefault();
        var (count, destPath) = storage.OffloadAccountToComputer(
            selectedAccountId,
            format: acc?.OffloadFormat ?? MailOffloadFormat.IndividualEml,
            accountEmail: acc?.EmailAddress);

        statusFooter.Text = $"💾 Выгружено писем на ПК: {count} ({destPath})";
        RefreshFolders();
        RefreshMessageList();
    }

    public void OpenMailFile(string filePath)
    {
        try
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".mbox")
            {
                var loaded = MailStorageEngine.LoadMbox(filePath);
                if (loaded.Count == 0)
                {
                    MessageBox.Show(this, "В выбранном файле архива не обнаружено писем.", "Pixel Mail", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                importedMessages = loaded;
                importedFileName = Path.GetFileName(filePath);
                selectedFolder = MailFolderKind.LocalArchive;
                RefreshFolders();
                RefreshMessageList();
                SelectMessage(loaded[0]);
            }
            else
            {
                var single = MailStorageEngine.LoadSingleEml(filePath);
                importedMessages = [single];
                importedFileName = Path.GetFileName(filePath);
                selectedFolder = MailFolderKind.LocalArchive;
                RefreshFolders();
                RefreshMessageList();
                SelectMessage(single);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Не удалось открыть файл:\n{ex.Message}", "Ошибка открытия письма", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteSelected()
    {
        if (selectedMessage == null) return;
        storage.DeleteMessage(selectedMessage.Id);
        selectedMessage = null;
        RefreshFolders();
        RefreshMessageList();
        RenderReadingPane(null);
    }

    internal void RefreshMailboxesAndMessages()
    {
        RefreshAccountsDropdown();
        RefreshFolders();
        RefreshFilterPills();
        RefreshMessageList();
    }

    internal void SelectAndOpenMessage(MailMessageItem message)
    {
        selectedAccountId = null;
        selectedFolder = message.Folder;
        RefreshAccountsDropdown();
        RefreshFolders();
        RefreshMessageList();
        SelectMessage(message);
    }
}
