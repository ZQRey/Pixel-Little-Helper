using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;

namespace PixelHelper;

public static class AntiTheftService
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    private static CancellationTokenSource? desktopWorkerCts;
    private static readonly object syncGate = new();

    public static void StartDesktopWorker(Settings settings)
    {
        AntiTheftLockdownState.RestoreForCurrentSession();
        lock (syncGate)
        {
            if (desktopWorkerCts != null) return;
            desktopWorkerCts = new();
            _ = Task.Run(() => WorkerLoopAsync(settings.ServerUrl, desktopWorkerCts.Token, isDesktopApp: true));
        }
    }

    public static void StopDesktopWorker()
    {
        lock (syncGate)
        {
            desktopWorkerCts?.Cancel();
            desktopWorkerCts = null;
        }
    }

    public static async Task RunSystemWorkerAsync(CancellationToken token)
    {
        string? serverUrl = GetMachineServerUrl();
        await WorkerLoopAsync(serverUrl, token, isDesktopApp: false);
    }

    private static string? GetMachineServerUrl()
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(@"Software\PixelHelper");
            return key?.GetValue("ServerUrl") as string;
        }
        catch { return null; }
    }

    private static async Task WorkerLoopAsync(string? serverUrl, CancellationToken token, bool isDesktopApp)
    {
        if (string.IsNullOrWhiteSpace(serverUrl)) serverUrl = "https://helper.gp1.loc";
        serverUrl = serverUrl.TrimEnd('/');

        // Initial delay so startup isn't congested
        try { await Task.Delay(TimeSpan.FromSeconds(isDesktopApp ? 5 : 20), token); }
        catch (OperationCanceledException) { return; }

        int intervalSeconds = 60;
        bool stateConfirmed = false;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var response = await SendTelemetryAsync(serverUrl, token);
                if (response != null)
                {
                    stateConfirmed = true;
                    if (response.IsLockedDown)
                        AntiTheftLockdownState.Save(response);
                    else
                        AntiTheftLockdownState.Clear();

                    if (response.TriggerBitLockerRecovery)
                    {
                        TriggerBitLockerRecovery();
                    }

                    if (response.LockSession)
                    {
                        LockWorkStation();
                    }

                    if (response.IsLockedDown && (response.LockScreen || (!response.LockScreen && !response.LockSession && !response.TriggerBitLockerRecovery)))
                    {
                        intervalSeconds = 15; // Faster heartbeat during active lockdown / recovery
                        if (isDesktopApp)
                        {
                            Application.Current?.Dispatcher?.InvokeAsync(() =>
                            {
                                AntiTheftLockWindow.EnsureShown(response.LockMessage);
                            });
                        }
                    }
                    else
                    {
                        intervalSeconds = 60;
                        if (!response.IsLockedDown)
                            AntiTheftLockdownState.Clear();
                        if (isDesktopApp)
                        {
                            Application.Current?.Dispatcher?.InvokeAsync(() =>
                            {
                                AntiTheftLockWindow.CloseIfOpen();
                            });
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Settings.Log("AntiTheft heartbeat failed: " + ex.Message);
                if (!stateConfirmed && isDesktopApp)
                    AntiTheftLockdownState.RestoreForCurrentSession();
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public static async Task<AntiTheftTelemetryResponse?> SendTelemetryAsync(string serverUrl, CancellationToken token = default)
    {
        var (connectedSsid, connectedBssid, visibleNetworks) = ScanWifiNetworks();
        var (batteryPercent, isCharging, hasBattery) = SystemInspector.GetBatteryStatus();
        bool isPortable = false;
        try { isPortable = Settings.Load().IsPortable; }
        catch { isPortable = SystemInspector.DetectIsPortable(); }
        string hardwareUuid = SystemInspector.GetHardwareUuid();
        bool wireGuardActive = WireGuardCrypto.IsTunnelActive();

        var telemetry = new AntiTheftTelemetryRequest(
            MachineName: Environment.MachineName,
            HardwareUuid: hardwareUuid,
            IsPortable: isPortable,
            BatteryPercent: batteryPercent,
            IsCharging: isCharging,
            ConnectedSsid: connectedSsid,
            ConnectedBssid: connectedBssid,
            VisibleNetworks: visibleNetworks,
            ClientVersion: SystemInspector.ClientVersion(),
            WireGuardActive: wireGuardActive
        );

        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(15)
        };

        var res = await client.PostAsJsonAsync("api/telemetry/anti-theft", telemetry, token);
        if (!res.IsSuccessStatusCode) return null;

        return await res.Content.ReadFromJsonAsync<AntiTheftTelemetryResponse>(cancellationToken: token);
    }

    public static AntiTheftTelemetryResponse? ReadLockdownState()
    {
        try
        {
            string path = Path.Combine(Settings.Folder, "anti-theft-lockdown.json");
            if (!File.Exists(path)) return null;
            return System.Text.Json.JsonSerializer.Deserialize<AntiTheftTelemetryResponse>(File.ReadAllText(path));
        }
        catch { return null; }
    }

    public static async Task<AntiTheftTelemetryResponse?> ReadLockdownStateAsync(string serverUrl, CancellationToken token = default)
    {
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(15) };
            using var response = await client.PostAsJsonAsync("api/telemetry/anti-theft", new AntiTheftTelemetryRequest(
                Environment.MachineName, SystemInspector.GetHardwareUuid(), SystemInspector.DetectIsPortable(), 0, false, "", ""), token);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<AntiTheftTelemetryResponse>(cancellationToken: token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { Settings.Log("Unable to refresh anti-theft state: " + ex.Message); return null; }
    }

    public static void SaveLockdownState(AntiTheftTelemetryResponse response)
    {
        try
        {
            Directory.CreateDirectory(Settings.Folder);
            string path = Path.Combine(Settings.Folder, "anti-theft-lockdown.json");
            string temp = path + ".tmp";
            File.WriteAllText(temp, System.Text.Json.JsonSerializer.Serialize(response));
            File.Move(temp, path, true);
        }
        catch (Exception ex) { Settings.Log("Failed to save anti-theft lockdown state: " + ex.Message); }
    }

    public static void ClearLockdownState()
    {
        try { File.Delete(Path.Combine(Settings.Folder, "anti-theft-lockdown.json")); }
        catch (Exception ex) { Settings.Log("Failed to clear anti-theft lockdown state: " + ex.Message); }
    }

    public static void TriggerBitLockerRecovery()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c manage-bde -forcerecovery C:",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
            Settings.Log("BitLocker forced recovery triggered via manage-bde.");
        }
        catch (Exception ex)
        {
            Settings.Log("Failed to trigger BitLocker recovery: " + ex.Message);
        }
    }

    public static (string ConnectedSsid, string ConnectedBssid, List<VisibleWifiNetwork> VisibleNetworks) ScanWifiNetworks()
    {
        string connectedSsid = "";
        string connectedBssid = "";
        var visibleNetworks = new List<VisibleWifiNetwork>();

        try
        {
            // 1. Get currently connected Wi-Fi info
            string interfacesOutput = RunNetsh("wlan show interfaces");
            if (!string.IsNullOrWhiteSpace(interfacesOutput))
            {
                foreach (var rawLine in interfacesOutput.Split('\n'))
                {
                    string line = rawLine.Trim();
                    if (line.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(':', 2);
                        if (parts.Length == 2) connectedSsid = parts[1].Trim();
                    }
                    else if (line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(':', 2);
                        if (parts.Length == 2) connectedBssid = parts[1].Trim();
                    }
                }
            }

            // 2. Scan all visible Wi-Fi networks and BSSIDs
            string networksOutput = RunNetsh("wlan show networks mode=bssid");
            if (!string.IsNullOrWhiteSpace(networksOutput))
            {
                string currentSsid = "";
                string currentBssid = "";
                int currentSignal = 0;

                foreach (var rawLine in networksOutput.Split('\n'))
                {
                    string line = rawLine.Trim();
                    if (line.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(currentBssid))
                        {
                            visibleNetworks.Add(new VisibleWifiNetwork(currentSsid, currentBssid, currentSignal));
                            currentBssid = "";
                        }
                        var parts = line.Split(':', 2);
                        currentSsid = parts.Length == 2 ? parts[1].Trim() : "";
                    }
                    else if (line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(currentBssid))
                        {
                            visibleNetworks.Add(new VisibleWifiNetwork(currentSsid, currentBssid, currentSignal));
                        }
                        var parts = line.Split(':', 2);
                        currentBssid = parts.Length == 2 ? parts[1].Trim() : "";
                    }
                    else if (line.Contains('%') && (line.Contains("Signal", StringComparison.OrdinalIgnoreCase) || line.Contains("Сигнал", StringComparison.OrdinalIgnoreCase)))
                    {
                        var match = Regex.Match(line, @"(\d+)\s*%");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out int sig))
                        {
                            currentSignal = sig;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(currentBssid))
                {
                    visibleNetworks.Add(new VisibleWifiNetwork(currentSsid, currentBssid, currentSignal));
                }
            }
        }
        catch (Exception ex)
        {
            Settings.Log("Wifi scan error: " + ex.Message);
        }

        return (connectedSsid, connectedBssid, visibleNetworks);
    }

    private static string RunNetsh(string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.GetEncoding(866)
                }
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output;
        }
        catch { return ""; }
    }
}

internal static class AntiTheftLockdownState
{
    private static readonly object Gate = new();
    private static AntiTheftTelemetryResponse? current;

    internal static void Save(AntiTheftTelemetryResponse response)
    {
        lock (Gate)
        {
            current = response;
            AntiTheftService.SaveLockdownState(response);
            if (response.IsLockedDown && response.LockScreen)
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() => AntiTheftLockWindow.EnsureShown(response.LockMessage)));
            else
                Application.Current?.Dispatcher?.BeginInvoke(new Action(AntiTheftLockWindow.CloseIfOpen));
        }
    }

    internal static void Clear()
    {
        lock (Gate)
        {
            current = null;
            AntiTheftService.ClearLockdownState();
            Application.Current?.Dispatcher?.BeginInvoke(new Action(AntiTheftLockWindow.CloseIfOpen));
        }
    }

    internal static void RestoreForCurrentSession()
    {
        lock (Gate)
        {
            current ??= AntiTheftService.ReadLockdownState();
            if (current is { IsLockedDown: true, LockScreen: true } state)
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() => AntiTheftLockWindow.EnsureShown(state.LockMessage)));
        }
    }

    internal static async Task RefreshFromServerAsync(string serverUrl, CancellationToken token)
    {
        var response = await AntiTheftService.ReadLockdownStateAsync(serverUrl, token);
        if (response != null) Save(response);
    }
}

internal sealed class AntiTheftWindowsService : System.ServiceProcess.ServiceBase
{
    internal const string Name = "PixelAntiTheft";
    private CancellationTokenSource? stop;

    internal AntiTheftWindowsService()
    {
        ServiceName = Name;
        CanStop = true;
        AutoLog = false;
    }

    protected override void OnStart(string[] args)
    {
        stop = new();
        _ = Task.Run(() => AntiTheftService.RunSystemWorkerAsync(stop.Token));
    }

    protected override void OnStop() => stop?.Cancel();
}


