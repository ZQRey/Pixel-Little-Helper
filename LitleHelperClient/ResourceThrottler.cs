using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace PixelHelper;

public enum HardwareTier
{
    Low,      // <= 2 cores or <= 4 GB RAM
    Medium,   // 4 cores, 8 GB RAM
    High      // >= 6 cores, >= 16 GB RAM
}

public sealed class ResourceThrottler
{
    private readonly Settings settings;
    private long lastCpuCheckTick;
    private double lastMeasuredCpuUsage;
    private long prevIdleTime;
    private long prevKernelTime;
    private long prevUserTime;

    public HardwareTier Tier { get; private set; } = HardwareTier.Medium;
    public int ProcessorCount { get; private set; }
    public ulong TotalMemoryMb { get; private set; }
    public ulong AvailableMemoryMb { get; private set; }

    public ResourceThrottler(Settings settings)
    {
        this.settings = settings;
        DetectHardware();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus; // 0 = Offline, 1 = Online, 255 = Unknown
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    public void DetectHardware()
    {
        try
        {
            ProcessorCount = Environment.ProcessorCount;
            var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                TotalMemoryMb = memStatus.ullTotalPhys / (1024 * 1024);
                AvailableMemoryMb = memStatus.ullAvailPhys / (1024 * 1024);
            }
            else
            {
                TotalMemoryMb = 8192;
                AvailableMemoryMb = 4096;
            }

            if (ProcessorCount <= 2 || TotalMemoryMb <= 4096)
            {
                Tier = HardwareTier.Low;
            }
            else if (ProcessorCount >= 6 && TotalMemoryMb >= 12288)
            {
                Tier = HardwareTier.High;
            }
            else
            {
                Tier = HardwareTier.Medium;
            }

            GetSystemTimes(out prevIdleTime, out prevKernelTime, out prevUserTime);
            lastCpuCheckTick = Environment.TickCount64;
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
            Tier = HardwareTier.Medium;
            ProcessorCount = 4;
        }
    }

    public bool IsUserActivelyWorking()
    {
        try
        {
            var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (GetLastInputInfo(ref lii))
            {
                uint idleMs = (uint)Environment.TickCount - lii.dwTime;
                // If user pressed key or moved mouse within last 2.5 seconds, user is active
                return idleMs < 2500;
            }
        }
        catch { }
        return false;
    }

    public bool IsRunningOnBattery()
    {
        try
        {
            if (GetSystemPowerStatus(out var status))
            {
                return status.ACLineStatus == 0; // On Battery
            }
        }
        catch { }
        return false;
    }

    public double GetSystemCpuUsage()
    {
        long now = Environment.TickCount64;
        if (now - lastCpuCheckTick < 500)
        {
            return lastMeasuredCpuUsage;
        }

        try
        {
            if (GetSystemTimes(out long idleTime, out long kernelTime, out long userTime))
            {
                long idleDiff = idleTime - prevIdleTime;
                long kernelDiff = kernelTime - prevKernelTime;
                long userDiff = userTime - prevUserTime;
                long totalDiff = kernelDiff + userDiff;

                prevIdleTime = idleTime;
                prevKernelTime = kernelTime;
                prevUserTime = userTime;
                lastCpuCheckTick = now;

                if (totalDiff > 0)
                {
                    double usage = (double)(totalDiff - idleDiff) * 100.0 / totalDiff;
                    lastMeasuredCpuUsage = Math.Clamp(usage, 0.0, 100.0);
                    return lastMeasuredCpuUsage;
                }
            }
        }
        catch { }

        return lastMeasuredCpuUsage;
    }

    public async Task ThrottleAsync(CancellationToken token)
    {
        string profile = (settings.AntivirusPerformanceProfile ?? "auto").ToLowerInvariant();
        int maxCpu = settings.AntivirusMaxCpuPercent > 0 ? settings.AntivirusMaxCpuPercent : 25;
        bool pauseOnActivity = settings.AntivirusPauseOnUserActivity;

        // 1. User activity backoff
        if (pauseOnActivity && IsUserActivelyWorking())
        {
            // Give user full interactive smoothness without CPU competition
            await Task.Delay(40, token);
            return;
        }

        // 2. High system load backoff
        double cpu = GetSystemCpuUsage();
        if (cpu > 75.0)
        {
            // System is heavily loaded by other apps (browser, 1C, compilation)
            await Task.Delay(60, token);
            return;
        }

        // 3. Profile-based inter-file micro-delays
        int delayMs = 5;
        if (profile == "low" || (profile == "auto" && Tier == HardwareTier.Low))
        {
            delayMs = 25;
        }
        else if (profile == "balanced" || (profile == "auto" && Tier == HardwareTier.Medium))
        {
            delayMs = 10;
        }
        else if (profile == "high")
        {
            delayMs = 2;
        }

        // Adjust for maxCpu limit
        if (cpu > maxCpu)
        {
            delayMs += 20;
        }

        if (delayMs > 0)
        {
            await Task.Delay(delayMs, token);
        }
    }
}
