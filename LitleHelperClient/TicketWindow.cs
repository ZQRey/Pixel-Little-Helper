using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace PixelHelper;

public sealed record TicketBranch(int Id, string Name, bool IsActive = true, string ComputerPrefixes = "");
public sealed record TicketContext(List<TicketBranch> Branches, int? DetectedBranchId = null, string Room = "", bool IsPortable = false);

internal static class TicketBranchSelection
{
    internal static (int? Id, bool Automatic) Select(TicketContext context, string machineName, int? savedId)
    {
        var branches = context.Branches.Where(b => b.IsActive).ToList();
        if (context.IsPortable)
        {
            var portable = branches.FirstOrDefault(b => b.Name.Equals("Переносное устройство", StringComparison.OrdinalIgnoreCase));
            return portable == null ? (null, false) : (portable.Id, true);
        }
        if (context.DetectedBranchId is int detected && branches.Any(b => b.Id == detected))
            return (detected, true);
        var match = branches.SelectMany(b => (b.ComputerPrefixes ?? "")
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(prefix => (Branch: b, Prefix: prefix)))
            .OrderByDescending(p => p.Prefix.Length)
            .FirstOrDefault(p => machineName.Trim().StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase));
        if (match.Branch != null) return (match.Branch.Id, true);
        return (savedId is int saved && branches.Any(b => b.Id == saved) ? saved : null, false);
    }
}

internal sealed class TicketWindow : Window
{
    private readonly CancellationTokenSource lifetime;
    private readonly List<TicketAttachmentDto> attachments = [];
    internal event Action<int>? TicketCreated;
    internal event Action? Submitting;
    internal event Action? SubmissionFailed;
    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    internal TicketWindow(Func<string, string, int?, string, CancellationToken, Task<int>> createTicket, Func<CancellationToken, Task<TicketContext>> getContext, Settings settings, CancellationToken token, string initialDescription = "")
        : this((t, d, b, r, _, c) => createTicket(t, d, b, r, c), getContext, settings, token, initialDescription)
    {
    }

    internal TicketWindow(Func<string, string, int?, string, List<TicketAttachmentDto>?, CancellationToken, Task<int>> createTicket, Func<CancellationToken, Task<TicketContext>> getContext, Settings settings, CancellationToken token, string initialDescription = "")
    {
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        Title = "Обращение в ИТ";
        Width = 470;
        MinHeight = 520;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SourceInitialized += (_, _) =>
        {
            var work = SystemParameters.WorkArea;
            Left = work.Left + (work.Width - Width) / 2;
            Top = work.Top + (work.Height - Math.Min(Height, work.Height - 40)) / 2;
        };

        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E2E8F0"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(10),
            AllowDrop = true,
            Effect = new DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 4,
                Opacity = 0.16,
                Color = Color.FromRgb(15, 23, 42)
            }
        };

        var panel = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        card.Child = panel;
        Content = card;

        // Header with title and close button
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var closeBtn = new Button
        {
            Content = "×",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#64748B"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Width = 28,
            Height = 28,
            Cursor = Cursors.Hand,
            Padding = new Thickness(0, -2, 0, 0)
        };
        closeBtn.Click += (_, _) => Close();
        DockPanel.SetDock(closeBtn, Dock.Right);
        header.Children.Add(closeBtn);

        var titleBlock = new StackPanel();
        titleBlock.Children.Add(new TextBlock
        {
            Text = "🎫 Написать программистам",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#0F172A")
        });
        titleBlock.Children.Add(new TextBlock
        {
            Text = "Заявка поступит в дежурную ИТ-очередь",
            FontSize = 11,
            Foreground = Brush("#64748B"),
            Margin = new Thickness(0, 2, 0, 0)
        });
        header.Children.Add(titleBlock);
        panel.Children.Add(header);

        if (SystemInspector.IsTemporaryProfile() || SystemInspector.IsTemporaryUser(Environment.UserName))
        {
            var tempWarning = new Border
            {
                Background = Brush("#FEF2F2"),
                BorderBrush = Brush("#FCA5A5"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var warningText = new TextBlock
            {
                Text = "⚠️ Вход выполнен под временным профилем Windows. Заявка будет помечена для администратора.",
                FontSize = 11,
                Foreground = Brush("#991B1B"),
                TextWrapping = TextWrapping.Wrap
            };
            tempWarning.Child = warningText;
            panel.Children.Add(tempWarning);
        }

        // Branch selection
        panel.Children.Add(new TextBlock
        {
            Text = "Филиал",
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = Brush("#334155"),
            Margin = new Thickness(0, 0, 0, 3)
        });
        var branch = new ComboBox
        {
            DisplayMemberPath = "Name",
            SelectedValuePath = "Id",
            Height = 30,
            Background = Brush("#F8FAFC"),
            BorderBrush = Brush("#CBD5E1"),
            Margin = new Thickness(0, 0, 0, 6)
        };
        panel.Children.Add(branch);
        var branchHint = new TextBlock { FontSize = 11, Foreground = Brush("#64748B"), Margin = new Thickness(0, 0, 0, 4) };
        panel.Children.Add(branchHint);
        branch.SelectionChanged += (_, _) => branchHint.Text = branch.SelectedItem == null ? "Выберите филиал" : "";

        // Room number
        panel.Children.Add(new TextBlock
        {
            Text = "Кабинет / Место",
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = Brush("#334155"),
            Margin = new Thickness(0, 0, 0, 3)
        });
        var room = new TextBox
        {
            Text = settings.TicketRoom,
            MaxLength = 50,
            Height = 28,
            Background = Brush("#F1F5F9"),
            Foreground = Brush("#334155"),
            IsReadOnly = true,
            Focusable = false,
            Cursor = Cursors.Arrow,
            ToolTip = "Номер кабинета зафиксирован в настройках клиента",
            BorderBrush = Brush("#CBD5E1"),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 0, 6)
        };
        panel.Children.Add(room);

        // Problem description
        panel.Children.Add(new TextBlock
        {
            Text = "Описание проблемы",
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = Brush("#334155"),
            Margin = new Thickness(0, 0, 0, 3)
        });
        var input = new TextBox
        {
            Height = 90,
            MaxLength = 8000,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brush("#F8FAFC"),
            BorderBrush = Brush("#CBD5E1"),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 6)
        };
        panel.Children.Add(input);
        if (!string.IsNullOrWhiteSpace(initialDescription)) input.Text = initialDescription;

        // Attachments section
        var attachHeader = new DockPanel { Margin = new Thickness(0, 2, 0, 4) };
        var attachBtn = new Button
        {
            Content = "📎 Прикрепить файл",
            Background = Brush("#F1F5F9"),
            Foreground = Brush("#334155"),
            BorderBrush = Brush("#CBD5E1"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4, 8, 4),
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Cursor = Cursors.Hand
        };
        DockPanel.SetDock(attachBtn, Dock.Left);
        attachHeader.Children.Add(attachBtn);

        var attachHint = new TextBlock
        {
            Text = "до 5 файлов (до 20 МБ); скриншот по Ctrl+V",
            FontSize = 10.5,
            Foreground = Brush("#64748B"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        attachHeader.Children.Add(attachHint);
        panel.Children.Add(attachHeader);

        var attachmentsList = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        panel.Children.Add(attachmentsList);

        void RenderAttachments()
        {
            attachmentsList.Children.Clear();
            attachBtn.Content = attachments.Count > 0 ? $"📎 Прикрепить файл ({attachments.Count}/5)" : "📎 Прикрепить файл";
            attachBtn.IsEnabled = attachments.Count < 5;

            for (int i = 0; i < attachments.Count; i++)
            {
                int idx = i;
                var att = attachments[i];
                string ext = Path.GetExtension(att.FileName).ToLowerInvariant();
                bool isImg = ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";
                string icon = isImg ? "🖼️ " : "📄 ";

                var itemBorder = new Border
                {
                    Background = Brush("#F8FAFC"),
                    BorderBrush = Brush("#CBD5E1"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(6, 3, 6, 3),
                    Margin = new Thickness(0, 2, 6, 2)
                };

                var itemPanel = new StackPanel { Orientation = Orientation.Horizontal };
                string display = att.FileName.Length > 20 ? att.FileName[..18] + "…" : att.FileName;
                string sizeStr = (att.Content.Length / 1024.0).ToString("0.0") + " КБ";

                itemPanel.Children.Add(new TextBlock
                {
                    Text = $"{icon}{display} ({sizeStr})",
                    FontSize = 11,
                    Foreground = Brush("#1E293B"),
                    VerticalAlignment = VerticalAlignment.Center
                });

                var removeBtn = new Button
                {
                    Content = "×",
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brush("#EF4444"),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(4, 0, 0, 0),
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center
                };
                removeBtn.Click += (_, _) =>
                {
                    attachments.RemoveAt(idx);
                    RenderAttachments();
                };
                itemPanel.Children.Add(removeBtn);
                itemBorder.Child = itemPanel;
                attachmentsList.Children.Add(itemBorder);
            }
        }

        // Status text
        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Height = 32,
            Margin = new Thickness(0, 2, 0, 6),
            Foreground = Brush("#475569"),
            FontSize = 11.5
        };
        panel.Children.Add(status);

        void TryAddAttachment(string name, byte[] data)
        {
            if (attachments.Count >= 5)
            {
                status.Foreground = Brush("#DC2626");
                status.Text = "Достигнут максимум в 5 прикреплённых файлов.";
                return;
            }
            if (data.Length > 20 * 1024 * 1024)
            {
                status.Foreground = Brush("#DC2626");
                status.Text = $"Файл {name} превышает лимит 20 МБ.";
                return;
            }
            attachments.Add(new TicketAttachmentDto(name, data));
            status.Foreground = Brush("#475569");
            status.Text = $"Файл {name} добавлен.";
            RenderAttachments();
        }

        attachBtn.Click += (_, _) =>
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Выберите файл для прикрепления к заявке",
                    Multiselect = true,
                    Filter = "Все файлы (*.*)|*.*|Изображения (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Документы (*.pdf;*.docx;*.xlsx;*.txt;*.log)|*.pdf;*.docx;*.xlsx;*.txt;*.log"
                };
                if (dialog.ShowDialog() == true)
                {
                    foreach (var file in dialog.FileNames)
                    {
                        if (File.Exists(file))
                        {
                            byte[] bytes = File.ReadAllBytes(file);
                            TryAddAttachment(Path.GetFileName(file), bytes);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                status.Foreground = Brush("#DC2626");
                status.Text = "Ошибка выбора файла: " + ex.Message;
            }
        };

        // Clipboard screenshot Ctrl+V support
        void HandlePasteScreenshot(KeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                try
                {
                    if (Clipboard.ContainsImage())
                    {
                        var img = Clipboard.GetImage();
                        if (img != null)
                        {
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(img));
                            using var ms = new MemoryStream();
                            encoder.Save(ms);
                            byte[] bytes = ms.ToArray();
                            TryAddAttachment($"Скриншот_{DateTime.Now:HH-mm-ss}.png", bytes);
                            e.Handled = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Settings.Log(ex);
                }
            }
        }

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else HandlePasteScreenshot(e);
        };

        // Drag and drop support
        card.DragOver += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        };

        card.Drop += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var file in files)
                {
                    try
                    {
                        if (File.Exists(file))
                        {
                            byte[] bytes = File.ReadAllBytes(file);
                            TryAddAttachment(Path.GetFileName(file), bytes);
                        }
                    }
                    catch (Exception ex)
                    {
                        status.Foreground = Brush("#DC2626");
                        status.Text = "Ошибка перетаскивания: " + ex.Message;
                    }
                }
            }
        };

        // Buttons
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button
        {
            Content = "Отмена",
            Background = Brush("#F1F5F9"),
            Foreground = Brush("#475569"),
            BorderBrush = Brush("#E2E8F0"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 6, 14, 6),
            FontSize = 13,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var send = new Button
        {
            Content = "Отправить",
            Background = Brush("#4F46E5"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#4338CA"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 6, 16, 6),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand
        };
        send.IsEnabled = false;

        buttons.Children.Add(cancel);
        buttons.Children.Add(send);
        panel.Children.Add(buttons);

        cancel.Click += (_, _) => Close();
        send.Click += async (_, _) =>
        {
            send.IsEnabled = false;
            attachBtn.IsEnabled = false;
            status.Foreground = Brush("#4F46E5");
            status.Text = attachments.Count > 0 ? "Отправка заявки и файлов…" : "Отправка заявки…";
            try
            {
                if (string.IsNullOrWhiteSpace(input.Text)) throw new ArgumentException("Введите текст проблемы");
                var selected = branch.SelectedItem as TicketBranch;
                if (branch.Items.Count > 0 && selected == null) throw new ArgumentException("Выберите филиал");
                if (selected != null && string.IsNullOrWhiteSpace(room.Text)) throw new ArgumentException("Укажите кабинет");
                Submitting?.Invoke();
                string fullName = SystemInspector.GetUserFullName();
                bool isTemp = SystemInspector.IsTemporaryProfile() || SystemInspector.IsTemporaryUser(Environment.UserName);
                string title;
                string desc = input.Text.Trim();
                if (isTemp)
                {
                    title = !string.IsNullOrWhiteSpace(fullName) && !SystemInspector.IsTemporaryUser(fullName)
                        ? $"[TEMP профиль] Заявка от {fullName}"
                        : $"[TEMP профиль] Заявка от ПК {Environment.MachineName}";
                    if (!desc.Contains("[Временный профиль", StringComparison.OrdinalIgnoreCase))
                    {
                        desc = $"[ВНИМАНИЕ: На ПК зафиксирован временный профиль Windows (TEMP). Заявитель: {fullName}]\n\n" + desc;
                    }
                }
                else
                {
                    title = !string.IsNullOrWhiteSpace(fullName) && !fullName.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase)
                        ? $"Заявка от {fullName} ({Environment.UserName})"
                        : $"Заявка от {Environment.UserName}";
                }
                string currentRoom = settings.TicketRoom?.Trim() ?? "";
                if (!CabinetValidation.Valid(currentRoom, Environment.MachineName))
                    throw new InvalidOperationException("Номер кабинета не указан или некорректен.");

                int id = await createTicket(title, desc, selected?.Id, currentRoom, attachments.Count > 0 ? attachments : null, lifetime.Token);
                if (lifetime.IsCancellationRequested) return;
                settings.TicketBranchId = selected?.Id; settings.TicketRoom = currentRoom;
                try { settings.Save(); } catch (Exception ex) { Settings.Log(ex); }
                input.Clear();
                attachments.Clear();
                RenderAttachments();
                status.Foreground = Brush("#16A34A");
                status.Text = $"✓ Заявка №{id} успешно создана!";
                TicketCreated?.Invoke(id);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Settings.Log(ex);
                status.Foreground = Brush("#DC2626");
                status.Text = "Не отправлено: " + ex.Message;
                SubmissionFailed?.Invoke();
            }
            finally
            {
                if (!lifetime.IsCancellationRequested)
                {
                    send.IsEnabled = true;
                    attachBtn.IsEnabled = attachments.Count < 5;
                }
            }
        };

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            NativeMethods.ToolWindow(handle, false);
            NativeMethods.SetWindowPos(handle, new nint(-1), 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
        };
        Loaded += async (_, _) =>
        {
            Topmost = true;
            Activate();
            input.Focus();
            status.Foreground = Brush("#64748B");
            status.Text = "Загрузка филиалов…";
            try
            {
                var context = await getContext(lifetime.Token);
                if (lifetime.IsCancellationRequested) return;
                branch.ItemsSource = context.Branches.Where(b => b.IsActive).ToList();
                var selection = TicketBranchSelection.Select(context, Environment.MachineName, settings.TicketBranchId);
                branch.SelectedValue = selection.Id;
                branchHint.Text = selection.Automatic ? "Определён автоматически по имени ПК" :
                    selection.Id == null ? "Выберите филиал" : "";
                if (!string.IsNullOrWhiteSpace(context.Room)) room.Text = context.Room;
                status.Text = ""; send.IsEnabled = true;
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Settings.Log(ex);
                status.Foreground = Brush("#DC2626");
                status.Text = "Не удалось загрузить филиалы. " + ex.Message;
            }
        };
        Closed += (_, _) => { lifetime.Cancel(); lifetime.Dispose(); };
    }
}
