using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace PixelHelper;

internal sealed class TicketWindow : Window
{
    private readonly CancellationTokenSource lifetime;
    private HwndSource? source;
    internal event Action<int>? TicketCreated;
    internal TicketWindow(Func<string, string, CancellationToken, Task<int>> createTicket, CancellationToken token)
    {
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        Title = "Обращение в ИТ"; Width = 370; Height = 270;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = null; ShowInTaskbar = false;
        var panel = new StackPanel { Margin = new Thickness(12) };
        Content = new Border { Background = Brushes.AliceBlue, BorderBrush = Brushes.SteelBlue, BorderThickness = new Thickness(3), Child = panel };
        panel.Children.Add(new TextBlock { Text = "Написать программистам", FontSize = 16, Foreground = Brushes.MidnightBlue, Margin = new Thickness(0, 0, 0, 8) });
        var input = new TextBox { Height = 112, MaxLength = 8000, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(input);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Height = 44, Margin = new Thickness(0, 6, 0, 0), Foreground = Brushes.MidnightBlue };
        panel.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var send = PetWindow.MakeButton("Отправить"); var cancel = PetWindow.MakeButton("Закрыть");
        cancel.Margin = new Thickness(12, 0, 0, 0);
        buttons.Children.Add(send); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        cancel.Click += (_, _) => Close();
        send.Click += async (_, _) =>
        {
            send.IsEnabled = false; status.Text = "Отправка…";
            try
            {
                if (string.IsNullOrWhiteSpace(input.Text)) throw new ArgumentException("Введите текст проблемы");
                int id = await createTicket("Заявка от " + Environment.UserName, input.Text.Trim(), lifetime.Token);
                if (lifetime.IsCancellationRequested) return;
                input.Clear(); status.Text = $"Заявка №{id} успешно создана!";
                TicketCreated?.Invoke(id);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex) { Settings.Log(ex); status.Text = "Не отправлено. " + ex.Message; }
            finally { if (!lifetime.IsCancellationRequested) send.IsEnabled = true; }
        };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            NativeMethods.ToolWindow(handle, false);
            source = HwndSource.FromHwnd(handle); source.AddHook(Hook);
        };
        Loaded += (_, _) => { input.Focus(); NativeMethods.Bottom(new WindowInteropHelper(this).Handle); };
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
