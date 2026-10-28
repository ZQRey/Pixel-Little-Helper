using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelHelper;

internal sealed class MailComposeWindow : Window
{
    private readonly Settings settings;
    private readonly MailStorageEngine storage;
    private readonly AntivirusService? antivirus;
    private readonly List<ParsedAttachmentData> pendingAttachments = new();
    private readonly ComboBox accountBox;
    private readonly TextBox toBox;
    private readonly TextBox ccBox;
    private readonly TextBox subjectBox;
    private readonly TextBox bodyBox;
    private readonly CheckBox encryptCheck;
    private readonly PasswordBox encryptKeyBox;
    private readonly StackPanel attachmentsPanel;
    private readonly TextBlock statusText;

    internal MailComposeWindow(
        Settings settings,
        MailStorageEngine storage,
        AntivirusService? antivirus,
        string? initialTo = null,
        string? initialSubject = null,
        string? initialBody = null,
        string? preselectedAccountId = null)
    {
        this.settings = settings;
        this.storage = storage;
        this.antivirus = antivirus;

        Title = "Новое письмо — Pixel Mail";
        Width = 680;
        Height = 620;
        MinWidth = 540;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));

        var mainGrid = new Grid { Margin = new Thickness(16) };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var headerFields = new StackPanel();

        // Account selector
        var fromRow = CreateFieldRow("От кого:", out var fromHost);
        accountBox = new ComboBox { Padding = new Thickness(8, 5, 8, 5) };
        var accounts = settings.MailAccounts.ToList();
        foreach (var acc in accounts)
        {
            string label = string.IsNullOrWhiteSpace(acc.DisplayName) ? acc.EmailAddress : $"{acc.DisplayName} <{acc.EmailAddress}>";
            accountBox.Items.Add(new ComboBoxItem { Content = label, Tag = acc });
        }
        int selectedIdx = 0;
        if (!string.IsNullOrEmpty(preselectedAccountId))
        {
            int idx = accounts.FindIndex(a => a.Id == preselectedAccountId);
            if (idx >= 0) selectedIdx = idx;
        }
        else
        {
            int defIdx = accounts.FindIndex(a => a.IsDefault);
            if (defIdx >= 0) selectedIdx = defIdx;
        }
        if (accountBox.Items.Count > 0) accountBox.SelectedIndex = selectedIdx;
        fromHost.Child = accountBox;
        headerFields.Children.Add(fromRow);

        // To
        var toRow = CreateFieldRow("Кому:", out var toHost);
        toBox = new TextBox { Padding = new Thickness(8, 5, 8, 5), Text = initialTo ?? "" };
        toHost.Child = toBox;
        headerFields.Children.Add(toRow);

        // Cc
        var ccRow = CreateFieldRow("Копия (Cc):", out var ccHost);
        ccBox = new TextBox { Padding = new Thickness(8, 5, 8, 5) };
        ccHost.Child = ccBox;
        headerFields.Children.Add(ccRow);

        // Subject
        var subjRow = CreateFieldRow("Тема:", out var subjHost);
        subjectBox = new TextBox { Padding = new Thickness(8, 5, 8, 5), Text = initialSubject ?? "" };
        subjHost.Child = subjectBox;
        headerFields.Children.Add(subjRow);

        // Formatting + Encryption bar
        var toolBar = new WrapPanel { Margin = new Thickness(0, 6, 0, 8) };
        foreach (var (lbl, tip, openTag, closeTag) in new[]
        {
            ("Ж", "Жирный (Ctrl+B)", "**", "**"),
            ("К", "Курсив (Ctrl+I)", "*", "*"),
            ("Ч", "Подчёркнутый (Ctrl+U)", "__", "__"),
            ("S", "Зачёркнутый (Ctrl+Shift+X)", "~~", "~~"),
            ("• Список", "Список", "• ", "")
        })
        {
            var fmtBtn = new Button
            {
                Content = lbl,
                ToolTip = tip,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 4, 0),
                FontWeight = FontWeights.SemiBold
            };
            string o = openTag, c = closeTag;
            fmtBtn.Click += (_, _) => WrapSelection(o, c);
            toolBar.Children.Add(fmtBtn);
        }

        var attachBtn = new Button
        {
            Content = "📎 Прикрепить файл",
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(8, 0, 8, 0)
        };
        attachBtn.Click += async (_, _) => await AddAttachmentAsync();
        toolBar.Children.Add(attachBtn);

        encryptCheck = new CheckBox
        {
            Content = "🔒 Зашифровать письмо (Pixel-AES-256)",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 8, 0),
            FontWeight = FontWeights.Medium
        };
        toolBar.Children.Add(encryptCheck);

        encryptKeyBox = new PasswordBox
        {
            Width = 140,
            Padding = new Thickness(4, 2, 4, 2),
            Password = settings.MailSharedEncryptionKey ?? "",
            ToolTip = "Ключ/пароль шифрования письма (если пусто — используется корпоративный ключ)"
        };
        toolBar.Children.Add(encryptKeyBox);

        headerFields.Children.Add(toolBar);
        Grid.SetRow(headerFields, 0);
        mainGrid.Children.Add(headerFields);

        // Body
        bodyBox = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(10),
            FontSize = 13.5,
            Text = initialBody ?? ""
        };
        bodyBox.PreviewKeyDown += (_, e) =>
        {
            if (MessageFormatting.TryApplyShortcut(bodyBox, e))
                e.Handled = true;
        };
        Grid.SetRow(bodyBox, 1);
        mainGrid.Children.Add(bodyBox);

        // Attachments list
        attachmentsPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
        Grid.SetRow(attachmentsPanel, 2);
        mainGrid.Children.Add(attachmentsPanel);

        // Bottom actions
        var bottomDock = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var buttonsPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        var draftBtn = new Button
        {
            Content = "📝 В черновики",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        draftBtn.Click += async (_, _) => await SubmitAsync(sendNow: false);

        var sendBtn = new Button
        {
            Content = "📤 Отправить",
            Padding = new Thickness(16, 6, 16, 6),
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold
        };
        sendBtn.Click += async (_, _) => await SubmitAsync(sendNow: true);

        buttonsPanel.Children.Add(draftBtn);
        buttonsPanel.Children.Add(sendBtn);
        DockPanel.SetDock(buttonsPanel, Dock.Right);
        bottomDock.Children.Add(buttonsPanel);

        statusText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        bottomDock.Children.Add(statusText);

        Grid.SetRow(bottomDock, 3);
        mainGrid.Children.Add(bottomDock);

        Content = mainGrid;
    }

    private static Grid CreateFieldRow(string label, out Border host)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var tb = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85))
        };
        Grid.SetColumn(tb, 0);
        g.Children.Add(tb);

        host = new Border();
        Grid.SetColumn(host, 1);
        g.Children.Add(host);
        return g;
    }

    private void WrapSelection(string prefix, string suffix)
    {
        int start = bodyBox.SelectionStart;
        int len = bodyBox.SelectionLength;
        string selected = len > 0 ? bodyBox.SelectedText : "текст";
        string replacement = prefix + selected + suffix;
        bodyBox.Text = bodyBox.Text.Remove(start, len).Insert(start, replacement);
        bodyBox.SelectionStart = start + prefix.Length;
        bodyBox.SelectionLength = selected.Length;
        bodyBox.Focus();
    }

    private async Task AddAttachmentAsync()
    {
        var dlg = new OpenFileDialog { Multiselect = true, Title = "Выберите файлы для прикрепления к письму" };
        if (dlg.ShowDialog(this) != true) return;

        foreach (string file in dlg.FileNames)
        {
            try
            {
                var fi = new FileInfo(file);
                if (fi.Length > 25 * 1024 * 1024)
                {
                    MessageBox.Show(this, $"Файл «{fi.Name}» превышает лимит 25 МБ.", "Ограничение размера", MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }

                byte[] bytes = await File.ReadAllBytesAsync(file);
                statusText.Text = $"🛡️ Проверка файла «{fi.Name}» антивирусом PixelGuard AV…";
                var scan = await MailSecurityScanner.ScanAttachmentBytesAsync(fi.Name, bytes, settings, antivirus);
                if (scan.IsThreat)
                {
                    statusText.Text = $"🚨 Вложение заблокировано PixelGuard AV: {scan.ThreatName}";
                    MessageBox.Show(
                        this,
                        $"Антивирус PixelGuard AV обнаружил угрозу в файле «{fi.Name}»:\n{scan.ThreatName}\n\nПрикрепление зараженного файла заблокировано.",
                        "Угроза заблокирована PixelGuard AV",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    continue;
                }

                pendingAttachments.Add(new ParsedAttachmentData(fi.Name, "application/octet-stream", bytes));
                statusText.Text = $"🛡️ Файл «{fi.Name}» проверен PixelGuard AV и прикреплён.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Ошибка чтения файла", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        RenderAttachments();
    }

    private void RenderAttachments()
    {
        attachmentsPanel.Children.Clear();
        if (pendingAttachments.Count == 0) return;

        var wrap = new WrapPanel();
        for (int i = 0; i < pendingAttachments.Count; i++)
        {
            int idx = i;
            var att = pendingAttachments[i];
            var chip = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 6, 4)
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = $"📎 {att.FileName} ({att.Content.Length / 1024.0:F1} КБ) ",
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center
            });
            var rmBtn = new Button
            {
                Content = "✕",
                Padding = new Thickness(3, 0, 3, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            rmBtn.Click += (_, _) =>
            {
                pendingAttachments.RemoveAt(idx);
                RenderAttachments();
            };
            row.Children.Add(rmBtn);
            chip.Child = row;
            wrap.Children.Add(chip);
        }
        attachmentsPanel.Children.Add(wrap);
    }

    private async Task SubmitAsync(bool sendNow)
    {
        if (accountBox.SelectedItem is not ComboBoxItem item || item.Tag is not MailAccountConfig account)
        {
            MessageBox.Show(this, "Сначала добавьте почтовый ящик в настройках почтового клиента.", "Нет почтового ящика", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var recipients = toBox.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (sendNow && recipients.Count == 0)
        {
            MessageBox.Show(this, "Укажите хотя бы одного получателя в поле «Кому».", "Получатель не указан", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ccList = ccBox.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var msg = new MailMessageItem
        {
            Subject = string.IsNullOrWhiteSpace(subjectBox.Text) ? "(Без темы)" : subjectBox.Text.Trim(),
            ToAddresses = recipients,
            CcAddresses = ccList,
            BodyText = bodyBox.Text,
            Snippet = bodyBox.Text.Length > 120 ? bodyBox.Text[..120] + "…" : bodyBox.Text,
            IsEncrypted = encryptCheck.IsChecked == true,
            EncryptionType = encryptCheck.IsChecked == true ? MailEncryptionMode.PixelAes256 : MailEncryptionMode.None
        };

        try
        {
            statusText.Text = sendNow ? "⏳ Отправка письма…" : "⏳ Сохранение черновика…";
            string encKey = !string.IsNullOrWhiteSpace(encryptKeyBox.Password) ? encryptKeyBox.Password : settings.MailSharedEncryptionKey;
            await storage.SaveOrSendOutgoingAsync(account, msg, pendingAttachments, sendNow, encKey);
            if (sendNow)
            {
                SoundManager.Play(SoundEvent.MessageSent, settings);
            }
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            statusText.Text = "Ошибка: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Ошибка отправки письма", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

