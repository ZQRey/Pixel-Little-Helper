using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PixelHelper;

internal sealed class FarewellLiquidationWindow : Window
{
    private readonly Sprites sprites = new();
    private readonly Image robotAvatar = new();
    private readonly TextBlock countdownBlock = new();
    private readonly DispatcherTimer timer = new();
    private readonly DispatcherTimer animTimer = new();

    private int cryFrameIndex;
    private int remainingSeconds;
    private bool canClose;
    private readonly bool autoSelfDestruct;

    public FarewellLiquidationWindow(int durationSeconds = 120, bool autoSelfDestruct = true)
    {
        this.remainingSeconds = Math.Max(1, durationSeconds);
        this.autoSelfDestruct = autoSelfDestruct;

        Title = Loc.T("FarewellTitle");
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = 460;
        Height = 340;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        var rootBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), // Slate 900
            BorderBrush = new SolidColorBrush(Color.FromRgb(244, 63, 94)), // Rose 500
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(24)
        };

        var mainStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Crying Avatar
        robotAvatar.Width = 64;
        robotAvatar.Height = 64;
        robotAvatar.HorizontalAlignment = HorizontalAlignment.Center;
        robotAvatar.Margin = new Thickness(0, 0, 0, 14);
        RenderOptions.SetBitmapScalingMode(robotAvatar, BitmapScalingMode.NearestNeighbor);
        UpdateAvatarFrame();
        mainStack.Children.Add(robotAvatar);

        // Title
        var titleBlock = new TextBlock
        {
            Text = Loc.T("FarewellTitle"),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(251, 113, 133)), // Rose 400
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        };
        mainStack.Children.Add(titleBlock);

        // Farewell message
        var msgBlock = new TextBlock
        {
            Text = Loc.T("FarewellLiquidationNotice"),
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            LineHeight = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 400,
            Margin = new Thickness(0, 0, 0, 16)
        };
        mainStack.Children.Add(msgBlock);

        // Countdown timer block
        countdownBlock.Text = Loc.Format("FarewellCountdown", remainingSeconds);
        countdownBlock.FontSize = 12;
        countdownBlock.FontWeight = FontWeights.SemiBold;
        countdownBlock.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)); // Slate 400
        countdownBlock.HorizontalAlignment = HorizontalAlignment.Center;
        mainStack.Children.Add(countdownBlock);

        rootBorder.Child = mainStack;
        Content = rootBorder;

        // Disallow closing
        Closing += (_, e) =>
        {
            if (!canClose)
            {
                e.Cancel = true;
            }
        };

        PreviewKeyDown += (_, e) =>
        {
            if (!canClose)
            {
                e.Handled = true;
            }
        };

        // Animation timer for crying frames
        animTimer.Interval = TimeSpan.FromMilliseconds(350);
        animTimer.Tick += (_, _) =>
        {
            cryFrameIndex = (cryFrameIndex + 1) % Sprites.FrameCount(PetState.Cry);
            UpdateAvatarFrame();
        };
        animTimer.Start();

        // 1-second countdown timer
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) => OnSecondTick();
        timer.Start();
    }

    private void UpdateAvatarFrame()
    {
        try
        {
            var frame = sprites.Get(PetState.Cry, cryFrameIndex);
            robotAvatar.Source = frame.Image;
        }
        catch
        {
            // Ignore in headless test environments if display device unavailable
        }
    }

    private void OnSecondTick()
    {
        remainingSeconds--;
        countdownBlock.Text = Loc.Format("FarewellCountdown", Math.Max(0, remainingSeconds));

        if (remainingSeconds <= 0)
        {
            timer.Stop();
            animTimer.Stop();
            canClose = true;

            if (autoSelfDestruct)
            {
                SelfDestructManager.ExecuteSelfDestruct();
            }
            else
            {
                Close();
            }
        }
    }

    public bool CanCloseNow => canClose;
    public int RemainingSeconds => remainingSeconds;
}
