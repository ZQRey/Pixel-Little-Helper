using System.IO;
using System.Windows.Threading;

namespace PixelHelper;

public sealed class DiskSpaceMonitor
{
    private readonly DispatcherTimer timer = new();
    private readonly Action<double> onLowSpace;
    private DateTime lastAlertTime = DateTime.MinValue;
    private readonly TimeSpan alertCooldown;
    private readonly string driveLetter;
    private readonly double thresholdPercent;

    public DiskSpaceMonitor(
        Action<double> onLowSpace,
        TimeSpan? checkInterval = null,
        TimeSpan? alertCooldown = null,
        string driveLetter = "C",
        double thresholdPercent = 10.0)
    {
        this.onLowSpace = onLowSpace;
        this.alertCooldown = alertCooldown ?? TimeSpan.FromHours(4);
        this.driveLetter = driveLetter;
        this.thresholdPercent = thresholdPercent;

        timer.Interval = checkInterval ?? TimeSpan.FromMinutes(30);
        timer.Tick += (_, _) => Check();
    }

    public void Start()
    {
        timer.Start();
        // Initial check after short delay
        Dispatcher.CurrentDispatcher.InvokeAsync(Check, DispatcherPriority.Background);
    }

    public void Stop() => timer.Stop();

    public static double? GetFreePercent(string driveLetter = "C")
    {
        try
        {
            var drive = new DriveInfo(driveLetter);
            if (!drive.IsReady || drive.TotalSize <= 0) return null;
            return (drive.AvailableFreeSpace * 100.0) / drive.TotalSize;
        }
        catch
        {
            return null;
        }
    }

    public void Check()
    {
        double? percent = GetFreePercent(driveLetter);
        if (percent.HasValue && percent.Value < thresholdPercent)
        {
            if (DateTime.UtcNow - lastAlertTime >= alertCooldown)
            {
                lastAlertTime = DateTime.UtcNow;
                onLowSpace(percent.Value);
            }
        }
    }
}
