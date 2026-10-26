using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PixelHelper;

public sealed record ScanResult(
    bool IsThreat,
    string FilePath,
    string ThreatName = "",
    string ThreatType = "",
    long FileSizeBytes = 0,
    string Sha256 = ""
);

public sealed record ScanSummary(
    int TotalFilesScanned,
    int ThreatsDetected,
    List<ScanResult> Threats,
    TimeSpan Elapsed
);

public sealed class AntivirusService : IDisposable
{
    private readonly Settings settings;
    private readonly HubConnectionService hub;
    private readonly ResourceThrottler throttler;
    private readonly QuarantineManager quarantine;
    private readonly CancellationTokenSource lifetime = new();
    private readonly HashSet<string> knownMalwareHashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object dbLock = new();

    public static string? OverrideDbDir { get; set; }
    private static string DbDir => OverrideDbDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PixelHelper", "av-db");
    private static string ManifestPath => Path.Combine(DbDir, "manifest.json");

    public ResourceThrottler Throttler => throttler;
    public QuarantineManager Quarantine => quarantine;

    public event Action<ScanResult>? ThreatFound;

    public AntivirusService(Settings settings, HubConnectionService hub)
    {
        this.settings = settings;
        this.hub = hub;
        this.throttler = new ResourceThrottler(settings);
        this.quarantine = new QuarantineManager();

        Directory.CreateDirectory(DbDir);
        LoadLocalDatabases();

        // Register hub event handlers
        hub.AntivirusSettingsReceived += OnSettingsUpdated;
        hub.AntivirusRestoreReceived += OnRestoreRequested;
        hub.AntivirusDeleteReceived += OnDeleteRequested;

        // Start background tasks
        Task.Run(BackgroundMaintenanceLoop);
    }

    private void OnSettingsUpdated(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("antivirusEnabled", out var avEn)) settings.AntivirusEnabled = avEn.GetBoolean();
            if (root.TryGetProperty("antivirusRetentionDays", out var ret)) settings.AntivirusRetentionDays = ret.GetInt32();
            if (root.TryGetProperty("performanceProfile", out var prof)) settings.AntivirusPerformanceProfile = prof.GetString() ?? "auto";
            if (root.TryGetProperty("maxCpuPercent", out var cpu)) settings.AntivirusMaxCpuPercent = cpu.GetInt32();
            if (root.TryGetProperty("pauseOnUserActivity", out var pause)) settings.AntivirusPauseOnUserActivity = pause.GetBoolean();
            if (root.TryGetProperty("excludedExtensions", out var ext)) settings.AntivirusExcludedExtensions = ext.GetString() ?? "";
            if (root.TryGetProperty("excludedPaths", out var pth)) settings.AntivirusExcludedPaths = pth.GetString() ?? "";
            settings.Save();
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    private void OnRestoreRequested(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string sha256 = root.GetProperty("sha256").GetString() ?? "";
            string filePath = root.GetProperty("filePath").GetString() ?? "";
            quarantine.RestoreFile(sha256, filePath);
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    private void OnDeleteRequested(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string sha256 = root.GetProperty("sha256").GetString() ?? "";
            string filePath = root.GetProperty("filePath").GetString() ?? "";
            quarantine.DeletePermanently(sha256, filePath);
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    private async Task BackgroundMaintenanceLoop()
    {
        while (!lifetime.Token.IsCancellationRequested)
        {
            try
            {
                // 1. Auto-purge expired quarantine items
                int purged = quarantine.RunAutoPurge();
                if (purged > 0)
                {
                    Settings.Log("PixelGuard AV: Автоматически удалено " + purged + " просроченных файлов из карантина.");
                }

                // 2. Check and sync virus database from server
                if (hub.IsOnline && settings.AntivirusEnabled)
                {
                    await CheckAndUpdateDatabasesAsync(lifetime.Token);
                }
            }
            catch (Exception ex) when (!lifetime.Token.IsCancellationRequested)
            {
                Settings.Log(ex);
            }

            // Check every 3 hours
            await Task.Delay(TimeSpan.FromHours(3), lifetime.Token);
        }
    }

    public async Task CheckAndUpdateDatabasesAsync(CancellationToken token)
    {
        try
        {
            string serverUrl = settings.ServerUrl?.TrimEnd('/') ?? "http://helper.gp1.loc";
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            http.DefaultRequestHeaders.Add("X-Client-Key", settings.ClientToken);
            http.DefaultRequestHeaders.Add("X-Machine-Name", Environment.MachineName);

            // Fetch manifest
            var manifestResp = await http.GetAsync(serverUrl + "/api/antivirus/manifest", token);
            if (!manifestResp.IsSuccessStatusCode) return;

            string manifestJson = await manifestResp.Content.ReadAsStringAsync(token);
            using var doc = JsonDocument.Parse(manifestJson);
            string remoteVer = doc.RootElement.GetProperty("version").GetString() ?? "";
            string remoteSha = doc.RootElement.GetProperty("bundleSha256").GetString() ?? "";

            string localVer = "";
            if (File.Exists(ManifestPath))
            {
                try
                {
                    using var localDoc = JsonDocument.Parse(File.ReadAllText(ManifestPath));
                    localVer = localDoc.RootElement.GetProperty("version").GetString() ?? "";
                }
                catch { }
            }

            if (remoteVer == localVer && !string.IsNullOrEmpty(localVer))
            {
                return; // Already up to date
            }

            // Download bundle
            var bundleResp = await http.GetAsync(serverUrl + "/api/antivirus/bundle", token);
            if (!bundleResp.IsSuccessStatusCode) return;

            string tempZip = Path.Combine(DbDir, "bundle.tmp.zip");
            await using (var fs = File.Create(tempZip))
            {
                await bundleResp.Content.CopyToAsync(fs, token);
            }

            // Verify SHA256
            string computedSha;
            using (var sha = SHA256.Create())
            await using (var fs = File.OpenRead(tempZip))
            {
                computedSha = Convert.ToHexString(await sha.ComputeHashAsync(fs, token));
            }

            if (!string.Equals(computedSha, remoteSha, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tempZip);
                return; // Checksum mismatch
            }

            // Extract bundle
            string extractDir = Path.Combine(DbDir, "current");
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            ZipFile.ExtractToDirectory(tempZip, extractDir);
            File.Delete(tempZip);

            await File.WriteAllTextAsync(ManifestPath, manifestJson, token);
            LoadLocalDatabases();
            Settings.Log("PixelGuard AV: Базы антивируса успешно обновлены до версии " + remoteVer);
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    private void LoadLocalDatabases()
    {
        lock (dbLock)
        {
            try
            {
                knownMalwareHashes.Clear();
                string hashesDir = Path.Combine(DbDir, "current", "hashes");
                if (Directory.Exists(hashesDir))
                {
                    foreach (var file in Directory.GetFiles(hashesDir, "*.txt"))
                    {
                        foreach (var line in File.ReadAllLines(file))
                        {
                            string trimmed = line.Trim();
                            if (trimmed.Length == 64 && !trimmed.StartsWith("#"))
                            {
                                knownMalwareHashes.Add(trimmed);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
            }
        }
    }

    public bool IsExcluded(string filePath, long fileSizeBytes)
    {
        // 1. Size limit
        long maxBytes = settings.AntivirusMaxScanSizeBytes > 0 ? settings.AntivirusMaxScanSizeBytes : 262144000;
        if (fileSizeBytes > maxBytes) return true;

        // 2. Extensions exclusion
        string ext = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(ext))
        {
            var excludedExts = (settings.AntivirusExcludedExtensions ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (excludedExts.Any(e => string.Equals(e.TrimStart('.'), ext.TrimStart('.'), StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        // 3. Paths exclusion
        string? excludedPaths = settings.AntivirusExcludedPaths;
        if (!string.IsNullOrWhiteSpace(excludedPaths))
        {
            var paths = excludedPaths.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var p in paths)
            {
                if (filePath.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }

        // 4. Restoration Whitelist (prevent double-quarantine)
        if (quarantine.IsWhitelisted(filePath))
        {
            return true;
        }

        return false;
    }

    public async Task<ScanResult> ScanFileAsync(string filePath, CancellationToken token)
    {
        await Task.Yield();
        if (!File.Exists(filePath)) return new ScanResult(false, filePath);

        var fi = new FileInfo(filePath);
        if (IsExcluded(filePath, fi.Length))
        {
            return new ScanResult(false, filePath);
        }

        // Tier 1: SHA-256 hash match (Abuse.ch MalwareBazaar)
        string sha256 = QuarantineManager.ComputeSha256(filePath);
        if (quarantine.IsWhitelisted(filePath, sha256))
        {
            return new ScanResult(false, filePath);
        }

        bool hashMatched;
        lock (dbLock)
        {
            hashMatched = knownMalwareHashes.Contains(sha256);
        }

        if (hashMatched)
        {
            return new ScanResult(true, filePath, "Trojan.MalwareBazaar.Generic", "Hash", fi.Length, sha256);
        }

        // Tier 2: Heuristic Analysis (Autorun worms, USB droppers, hidden scripts, double extensions)
        var heuristicResult = CheckHeuristics(filePath, fi);
        if (heuristicResult != null)
        {
            return heuristicResult with { FileSizeBytes = fi.Length, Sha256 = sha256 };
        }

        return new ScanResult(false, filePath, "", "", fi.Length, sha256);
    }

    private ScanResult? CheckHeuristics(string filePath, FileInfo fi)
    {
        string fileName = fi.Name.ToLowerInvariant();
        string ext = fi.Extension.ToLowerInvariant();

        // 1. Double extension trick (e.g. "report.pdf.exe", "document.docx.vbs")
        string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        if (nameWithoutExt.EndsWith(".pdf") || nameWithoutExt.EndsWith(".doc") ||
            nameWithoutExt.EndsWith(".docx") || nameWithoutExt.EndsWith(".xlsx") ||
            nameWithoutExt.EndsWith(".jpg") || nameWithoutExt.EndsWith(".png") ||
            nameWithoutExt.EndsWith(".zip"))
        {
            if (ext is ".exe" or ".vbs" or ".bat" or ".cmd" or ".ps1" or ".scr" or ".pif" or ".hta")
            {
                return new ScanResult(true, filePath, "Worm.DisguisedDoubleExtension", "Heuristic");
            }
        }

        // 2. Suspicious autorun.inf on removable media
        if (fileName == "autorun.inf")
        {
            try
            {
                string text = File.ReadAllText(filePath);
                if (Regex.IsMatch(text, @"\[autorun\]", RegexOptions.IgnoreCase) &&
                    (Regex.IsMatch(text, @"open\s*=", RegexOptions.IgnoreCase) ||
                     Regex.IsMatch(text, @"shellexecute\s*=", RegexOptions.IgnoreCase) ||
                     Regex.IsMatch(text, @"action\s*=", RegexOptions.IgnoreCase)))
                {
                    return new ScanResult(true, filePath, "Worm.Autorun.USB", "Heuristic");
                }
            }
            catch { }
        }

        // 3. Malicious script droppers (.vbs, .bat, .ps1, .js, .hta)
        if (ext is ".vbs" or ".bat" or ".ps1" or ".js" or ".hta" or ".wsf")
        {
            if (fi.Length < 1048576) // Only scan text scripts < 1 MB
            {
                try
                {
                    string content = File.ReadAllText(filePath);
                    if ((content.Contains("powershell", StringComparison.OrdinalIgnoreCase) &&
                         (content.Contains("-enc", StringComparison.OrdinalIgnoreCase) ||
                          content.Contains("-w hidden", StringComparison.OrdinalIgnoreCase) ||
                          content.Contains("Bypass", StringComparison.OrdinalIgnoreCase))) ||
                        (content.Contains("WScript.Shell", StringComparison.OrdinalIgnoreCase) &&
                         content.Contains(".Run", StringComparison.OrdinalIgnoreCase) &&
                         content.Contains("http", StringComparison.OrdinalIgnoreCase)))
                    {
                        return new ScanResult(true, filePath, "Dropper.Script.Heuristic", "Heuristic");
                    }
                }
                catch { }
            }
        }

        // 4. Hidden executables in root or Recycler on USB
        if (ext is ".exe" or ".scr")
        {
            if ((fi.Attributes & FileAttributes.Hidden) != 0 || (fi.Attributes & FileAttributes.System) != 0)
            {
                return new ScanResult(true, filePath, "Trojan.HiddenExecutable", "Heuristic");
            }
        }

        return null;
    }

    public async Task<ScanSummary> ScanDirectoryAsync(
        string rootPath,
        Action<string, int, int>? onProgress,
        CancellationToken token)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int scanned = 0;
        var threats = new List<ScanResult>();

        if (!Directory.Exists(rootPath))
        {
            return new ScanSummary(0, 0, threats, sw.Elapsed);
        }

        var files = new List<string>();
        try
        {
            files = Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }

        int total = files.Count;
        for (int i = 0; i < total; i++)
        {
            token.ThrowIfCancellationRequested();
            string file = files[i];
            scanned++;

            // Dynamic hardware-aware throttling
            await throttler.ThrottleAsync(token);

            try
            {
                var res = await ScanFileAsync(file, token);
                if (res.IsThreat)
                {
                    threats.Add(res);
                    ThreatFound?.Invoke(res);
                    await HandleDetectedThreatAsync(res);
                }
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
            }

            onProgress?.Invoke(file, scanned, total);
        }

        sw.Stop();
        return new ScanSummary(scanned, threats.Count, threats, sw.Elapsed);
    }

    public async Task<ScanSummary> ScanPathAsync(
        string targetPath,
        Action<string, int, int>? onProgress,
        CancellationToken token)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var threats = new List<ScanResult>();
        int scanned = 0;

        if (File.Exists(targetPath))
        {
            scanned = 1;
            onProgress?.Invoke(targetPath, 1, 1);
            var res = await ScanFileAsync(targetPath, token);
            if (res.IsThreat)
            {
                threats.Add(res);
                ThreatFound?.Invoke(res);
                await HandleDetectedThreatAsync(res);
            }
            sw.Stop();
            return new ScanSummary(scanned, threats.Count, threats, sw.Elapsed);
        }
        else if (Directory.Exists(targetPath))
        {
            return await ScanDirectoryAsync(targetPath, onProgress, token);
        }

        sw.Stop();
        return new ScanSummary(0, 0, threats, sw.Elapsed);
    }

    public async Task<ScanSummary> ScanRemovableDriveAsync(
        string driveLetter,
        Action<string>? onStatus,
        CancellationToken token)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int scanned = 0;
        var threats = new List<ScanResult>();
        string root = driveLetter.TrimEnd('\\') + "\\";

        onStatus?.Invoke("Экспресс-проверка корня накопителя " + driveLetter + "…");

        // 1. Express root scan (autorun, hidden droppers, lnk shortcuts)
        try
        {
            var rootFiles = Directory.GetFiles(root);
            foreach (var f in rootFiles)
            {
                token.ThrowIfCancellationRequested();
                scanned++;
                var res = await ScanFileAsync(f, token);
                if (res.IsThreat)
                {
                    threats.Add(res);
                    ThreatFound?.Invoke(res);
                    await HandleDetectedThreatAsync(res);
                }
            }
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }

        // 2. Deep background scan of remaining files
        onStatus?.Invoke("Фоновая проверка содержимого " + driveLetter + "…");
        try
        {
            var summary = await ScanDirectoryAsync(root, (file, current, total) =>
            {
                if (current % 15 == 0) onStatus?.Invoke($"Проверено файлов: {current} из {total}");
            }, token);

            scanned += summary.TotalFilesScanned;
            threats.AddRange(summary.Threats);
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }

        sw.Stop();
        return new ScanSummary(scanned, threats.Count, threats, sw.Elapsed);
    }

    public async Task HandleDetectedThreatAsync(ScanResult result)
    {
        try
        {
            // Isolate into quarantine
            int retention = settings.AntivirusRetentionDays;
            var item = quarantine.QuarantineFile(result.FilePath, result.ThreatName, result.ThreatType, retention);

            // Report to server
            if (hub.IsOnline)
            {
                string serverUrl = settings.ServerUrl?.TrimEnd('/') ?? "http://helper.gp1.loc";
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                http.DefaultRequestHeaders.Add("X-Client-Key", settings.ClientToken);
                http.DefaultRequestHeaders.Add("X-Machine-Name", Environment.MachineName);

                var payload = new
                {
                    machineName = Environment.MachineName,
                    userName = Environment.UserName,
                    filePath = result.FilePath,
                    threatName = result.ThreatName,
                    threatType = result.ThreatType,
                    fileSizeBytes = result.FileSizeBytes,
                    sha256 = result.Sha256,
                    actionTaken = "Quarantined"
                };

                await http.PostAsJsonAsync(serverUrl + "/api/antivirus/threats", payload);
            }
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    public void Dispose()
    {
        lifetime.Cancel();
        hub.AntivirusSettingsReceived -= OnSettingsUpdated;
        hub.AntivirusRestoreReceived -= OnRestoreRequested;
        hub.AntivirusDeleteReceived -= OnDeleteRequested;
    }
}
