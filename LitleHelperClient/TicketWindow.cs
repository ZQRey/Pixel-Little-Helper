using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PixelHelper;
public sealed record TicketBranch(int Id, string Name, bool IsActive = true);

internal sealed class TicketWindow : Window
{
    private readonly CancellationTokenSource lifetime;
    private HwndSource? source;
    internal event Action<int>? TicketCreated;
    internal event Action? Submitting;
    internal event Action? SubmissionFailed;
    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    internal TicketWindow(Func<string, string, int?, string, CancellationToken, Task<int>> createTicket, Func<CancellationToken, Task<List<TicketBranch>>> getBranches, Settings settings, CancellationToken token)
    {
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        Title = "Обращение в ИТ";
        Width = 430; Height = 460;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;

        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E2E8F0"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(10),
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
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
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
            Cursor = System.Windows.Input.Cursors.Hand,
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

        // Branch selection
        panel.Children.Add(new TextBlock
        {
            Text = "Филиал",
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = Brush("#334155"),
            Margin = new Thickness(0, 0, 0, 4)
        });
        var branch = new ComboBox
        {
            DisplayMemberPath = "Name",
            SelectedValuePath = "Id",
            Height = 32,
            Background = Brush("#F8FAFC"),
            BorderBrush = Brush("#CBD5E1"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(branch);

        // Room number
        panel.Children.Add(new TextBlock
        {
            Text = "Кабинет / Место",
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = Brush("#334155"),
            Margin = new Thickness(0, 0, 0, 4)
        });
        var room = new TextBox
        {
            Text = settings.TicketRoom,
            MaxLength = 100,
            Height = 30,
            Background = Brush("#F8FAFC"),
            BorderBrush = Brush("#CBD5E1"),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(room);

        // Problem description
        panel.Children.Add(new TextBlock
        {
            Text = "Описание проблемы",
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = Brush("#334155"),
            Margin = new Thickness(0, 0, 0, 4)
        });
        var input = new TextBox
        {
            Height = 105,
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

        // Status text
        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Height = 36,
            Margin = new Thickness(0, 2, 0, 6),
            Foreground = Brush("#475569"),
            FontSize = 12
        };
        panel.Children.Add(status);

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
            Cursor = System.Windows.Input.Cursors.Hand,
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
            Cursor = System.Windows.Input.Cursors.Hand
        };
        send.IsEnabled = false;

        buttons.Children.Add(cancel);
        buttons.Children.Add(send);
        panel.Children.Add(buttons);

        cancel.Click += (_, _) => Close();
        send.Click += async (_, _) =>
        {
            send.IsEnabled = false;
            status.Foreground = Brush("#4F46E5");
            status.Text = "Отправка заявки…";
            try
            {
                if (string.IsNullOrWhiteSpace(input.Text)) throw new ArgumentException("Введите текст проблемы");
                var selected = branch.SelectedItem as TicketBranch;
                if (branch.Items.Count > 0 && selected == null) throw new ArgumentException("Выберите филиал");
                if (selected != null && string.IsNullOrWhiteSpace(room.Text)) throw new ArgumentException("Укажите кабинет");
                Submitting?.Invoke();
                int id = await createTicket("Заявка от " + Environment.UserName, input.Text.Trim(), selected?.Id, room.Text.Trim(), lifetime.Token);
                if (lifetime.IsCancellationRequested) return;
                settings.TicketBranchId = selected?.Id; settings.TicketRoom = room.Text.Trim();
                try { settings.Save(); } catch (Exception ex) { Settings.Log(ex); }
                input.Clear();
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
            finally { if (!lifetime.IsCancellationRequested) send.IsEnabled = true; }
        };

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            NativeMethods.ToolWindow(handle, false);
            source = HwndSource.FromHwnd(handle); source.AddHook(Hook);
        };
        Loaded += async (_, _) =>
        {
            input.Focus(); NativeMethods.Bottom(new WindowInteropHelper(this).Handle);
            status.Foreground = Brush("#64748B");
            status.Text = "Загрузка филиалов…";
            try
            {
                branch.ItemsSource = await getBranches(lifetime.Token);
                if (lifetime.IsCancellationRequested) return;
                branch.SelectedValue = settings.TicketBranchId;
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
        Closed += (_, _) => { lifetime.Cancel(); source?.RemoveHook(Hook); lifetime.Dispose(); };
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
    }

    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.WM_WINDOWPOSCHANGING)
        {
            var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
            if ((pos.Flags & NativeMethods.SWP_NOZORDER) == 0)
            {
                pos.HwndInsertAfter = NativeMethods.DesktopAnchor(hwnd);
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        return 0;
    }
}
