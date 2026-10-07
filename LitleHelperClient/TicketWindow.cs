using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace PixelHelper;
public sealed record TicketBranch(int Id, string Name, bool IsActive = true);

internal sealed class TicketWindow : Window
{
    private readonly CancellationTokenSource lifetime;
    private HwndSource? source;
    internal event Action<int>? TicketCreated;
    internal event Action? Submitting;
    internal event Action? SubmissionFailed;
    internal TicketWindow(Func<string, string, int?, string, CancellationToken, Task<int>> createTicket, Func<CancellationToken, Task<List<TicketBranch>>> getBranches, Settings settings, CancellationToken token)
    {
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        Title = "Обращение в ИТ"; Width = 410; Height = 410;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = null; ShowInTaskbar = false;
        var panel = new StackPanel { Margin = new Thickness(12) };
        Content = new Border { Background = Brushes.AliceBlue, BorderBrush = Brushes.SteelBlue, BorderThickness = new Thickness(3), Child = panel };
        panel.Children.Add(new TextBlock { Text = "Написать программистам", FontSize = 16, Foreground = Brushes.MidnightBlue, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(new TextBlock { Text = "Филиал", Margin = new Thickness(0, 0, 0, 4) });
        var branch = new ComboBox { DisplayMemberPath = "Name", SelectedValuePath = "Id", Height = 30 };
        panel.Children.Add(branch);
        panel.Children.Add(new TextBlock { Text = "Кабинет", Margin = new Thickness(0, 8, 0, 4) });
        var room = new TextBox { Text = settings.TicketRoom, MaxLength = 100, Height = 28, Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(room);
        var input = new TextBox { Height = 112, MaxLength = 8000, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(input);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Height = 44, Margin = new Thickness(0, 6, 0, 0), Foreground = Brushes.MidnightBlue };
        panel.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var send = PetWindow.MakeButton("Отправить"); var cancel = PetWindow.MakeButton("Закрыть");
        send.IsEnabled = false;
        cancel.Margin = new Thickness(12, 0, 0, 0);
        buttons.Children.Add(send); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        cancel.Click += (_, _) => Close();
        send.Click += async (_, _) =>
        {
            send.IsEnabled = false; status.Text = "Отправка…";
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
                input.Clear(); status.Text = $"Заявка №{id} успешно создана!";
                TicketCreated?.Invoke(id);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex) { Settings.Log(ex); status.Text = "Не отправлено. " + ex.Message; SubmissionFailed?.Invoke(); }
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
            input.Focus(); NativeMethods.Bottom(new WindowInteropHelper(this).Handle); status.Text = "Загрузка филиалов…";
            try
            {
                branch.ItemsSource = await getBranches(lifetime.Token);
                if (lifetime.IsCancellationRequested) return;
                branch.SelectedValue = settings.TicketBranchId;
                status.Text = ""; send.IsEnabled = true;
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex) { Settings.Log(ex); status.Text = "Не удалось загрузить филиалы. " + ex.Message; }
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
