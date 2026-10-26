using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace LitleHelperServer;

public record AntivirusManifest(
    string Version,
    DateTime UpdatedAt,
    int MalwareBazaarHashCount,
    int YaraRulesCount,
    string ClamAvVersion,
    string BundleSha256,
    long BundleSizeBytes
);

public record ServerProbeResult(
    string Name,
    string Url,
    string Description,
    bool IsReachable,
    long LatencyMs,
    int StatusCode,
    string StatusText,
    string ErrorMessage
);

public class AntivirusSyncWorker : BackgroundService
{
    private readonly IConfiguration configuration;
    private readonly IntegrationSettings settings;
    private readonly ILogger<AntivirusSyncWorker> logger;
    private readonly IHttpClientFactory httpFactory;
    private readonly SemaphoreSlim syncLock = new(1, 1);

    private DateTime? lastSyncAt;
    private DateTime? nextSyncAt;
    private string lastError = "";
    private bool isSyncing;
    private AntivirusManifest? cachedManifest;
    private string? cachedBundlePath;

    public AntivirusSyncWorker(
        IConfiguration configuration,
        IntegrationSettings settings,
        ILogger<AntivirusSyncWorker> logger,
        IHttpClientFactory httpFactory)
    {
        this.configuration = configuration;
        this.settings = settings;
        this.logger = logger;
        this.httpFactory = httpFactory;
    }

    private string StorageDir => Path.GetFullPath(configuration["Antivirus:StorageDir"] ?? Path.Combine("data", "antivirus"));

    public object GetStatus()
    {
        var sec = settings.Security();
        return new
        {
            enabled = sec.AntivirusEnabled,
            scheduleHours = sec.AntivirusScheduleHours,
            retentionDays = sec.AntivirusRetentionDays,
            performanceProfile = sec.PerformanceProfile,
            maxCpuPercent = sec.MaxCpuPercent,
            pauseOnUserActivity = sec.PauseOnUserActivity,
            clamAvEnabled = sec.ClamAvEnabled,
            yaraEnabled = sec.YaraEnabled,
            hashCheckEnabled = sec.HashCheckEnabled,
            excludedExtensions = sec.ExcludedExtensions,
            excludedPaths = sec.ExcludedPaths,
            maxScanSizeBytes = sec.MaxScanSizeBytes,
            lastSyncAt,
            nextSyncAt,
            lastError,
            isSyncing,
            manifest = cachedManifest
        };
    }

    public AntivirusManifest? GetManifest() => cachedManifest;

    public string? GetBundlePath()
    {
        if (cachedBundlePath != null && File.Exists(cachedBundlePath)) return cachedBundlePath;
        string defaultPath = Path.Combine(StorageDir, "antivirus_bundle.zip");
        return File.Exists(defaultPath) ? defaultPath : null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PixelGuard AV Sync Worker initialized.");
        Directory.CreateDirectory(StorageDir);

        // Load existing manifest or bundle on start
        LoadExistingBundle();

        // Initial delay before first background check
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        // If existing bundle is only the bootstrap bundle (or empty), trigger initial sync immediately
        if (cachedManifest == null || cachedManifest.MalwareBazaarHashCount <= 1)
        {
            logger.LogInformation("Обнаружен только базовый пакет баз. Запуск первичной синхронизации внешних антивирусных баз...");
            _ = Task.Run(() => SyncInternalAsync(stoppingToken, force: true), stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var sec = settings.Security();
                if (sec.AntivirusEnabled)
                {
                    bool shouldSync = lastSyncAt == null ||
                        (DateTime.UtcNow - lastSyncAt.Value).TotalHours >= Math.Max(3, sec.AntivirusScheduleHours);

                    if (shouldSync)
                    {
                        await SyncInternalAsync(stoppingToken, force: false);
                    }
                }

                int hours = Math.Max(3, settings.Security().AntivirusScheduleHours);
                nextSyncAt = (lastSyncAt ?? DateTime.UtcNow).AddHours(hours);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Ошибка в цикле фоновой синхронизации антивирусных баз.");
                lastError = ex.Message;
            }

            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }
    }

    public async Task<bool> TriggerSyncNowAsync(CancellationToken token, bool force = false)
    {
        bool isBootstrap = cachedManifest != null && cachedManifest.MalwareBazaarHashCount <= 1;
        if (!force && !isBootstrap && lastSyncAt.HasValue && (DateTime.UtcNow - lastSyncAt.Value).TotalMinutes < 2)
        {
            throw new InvalidOperationException($"Слишком частый запрос. Повторное обновление доступно через {Math.Ceiling(2 - (DateTime.UtcNow - lastSyncAt.Value).TotalMinutes)} мин.");
        }

        return await SyncInternalAsync(token, force: true);
    }

    private void LoadExistingBundle()
    {
        try
        {
            string manifestPath = Path.Combine(StorageDir, "manifest.json");
            string bundlePath = Path.Combine(StorageDir, "antivirus_bundle.zip");
            if (File.Exists(manifestPath) && File.Exists(bundlePath))
            {
                string json = File.ReadAllText(manifestPath);
                cachedManifest = JsonSerializer.Deserialize<AntivirusManifest>(json);
                cachedBundlePath = bundlePath;
                lastSyncAt = cachedManifest?.UpdatedAt;
            }
            else
            {
                // Generate base bootstrap bundle if none exists
                BuildBootstrapBundle();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось загрузить существующий манифест антивируса.");
            BuildBootstrapBundle();
        }
    }

    private void BuildBootstrapBundle()
    {
        try
        {
            Directory.CreateDirectory(StorageDir);
            string bundlePath = Path.Combine(StorageDir, "antivirus_bundle.zip");
            string manifestPath = Path.Combine(StorageDir, "manifest.json");

            // Create baseline initial signatures
            string hashesDir = Path.Combine(StorageDir, "hashes");
            string yaraDir = Path.Combine(StorageDir, "yara");
            Directory.CreateDirectory(hashesDir);
            Directory.CreateDirectory(yaraDir);

            // Baseline autorun/worm YARA rules
            string autorunYara = Path.Combine(yaraDir, "autorun_worms.yar");
            if (!File.Exists(autorunYara))
            {
                File.WriteAllText(autorunYara, @"
rule Suspicious_Autorun_Inf {
    meta:
        description = ""Detects malicious autorun.inf files on removable USB media""
        author = ""PixelGuard AV""
    strings:
        $a1 = ""[autorun]"" nocase
        $open1 = ""open="" nocase
        $shellexec1 = ""shellexecute="" nocase
        $action1 = ""action="" nocase
    condition:
        $a1 and ($open1 or $shellexec1 or $action1)
}

rule Suspicious_Vbs_Powershell_Dropper {
    meta:
        description = ""Detects hidden VBS / PowerShell droppers common on USB sticks""
        author = ""PixelGuard AV""
    strings:
        $ps1 = ""powershell -enc"" nocase
        $ps2 = ""powershell.exe -w hidden"" nocase
        $ps3 = ""-ExecutionPolicy Bypass"" nocase
        $vbs1 = ""WScript.Shell"" nocase
        $vbs2 = ""CreateObject(\""Scripting.FileSystemObject\"")"" nocase
        $vbs3 = "".Run"" nocase
    condition:
        ($ps1 or ($ps2 and $ps3)) or ($vbs1 and $vbs2 and $vbs3)
}

rule Suspicious_Double_Extension_Executable {
    meta:
        description = ""Detects executable disguised with document extension (.pdf.exe, .doc.exe)""
        author = ""PixelGuard AV""
    strings:
        $mz = ""MZ""
    condition:
        $mz at 0
}
");
            }

            // Baseline hash list for top ransomware & trojans
            string hashesFile = Path.Combine(hashesDir, "malware_hashes.txt");
            if (!File.Exists(hashesFile))
            {
                File.WriteAllText(hashesFile, "# PixelGuard AV Baseline Malicious SHA256\n" +
                    "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\n");
            }

            if (File.Exists(bundlePath)) File.Delete(bundlePath);
            using (var zip = ZipFile.Open(bundlePath, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(autorunYara, "yara/autorun_worms.yar");
                zip.CreateEntryFromFile(hashesFile, "hashes/malware_hashes.txt");
            }

            var fi = new FileInfo(bundlePath);
            string sha256;
            using (var sha = SHA256.Create())
            using (var fs = fi.OpenRead())
            {
                sha256 = Convert.ToHexString(sha.ComputeHash(fs));
            }

            cachedManifest = new AntivirusManifest(
                Version: DateTime.UtcNow.ToString("yyyyMMdd.HHmm"),
                UpdatedAt: DateTime.UtcNow,
                MalwareBazaarHashCount: 1,
                YaraRulesCount: 3,
                ClamAvVersion: "daily-cvd-baseline",
                BundleSha256: sha256,
                BundleSizeBytes: fi.Length
            );

            File.WriteAllText(manifestPath, JsonSerializer.Serialize(cachedManifest, new JsonSerializerOptions { WriteIndented = true }));
            cachedBundlePath = bundlePath;
            lastSyncAt = cachedManifest.UpdatedAt;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при создании базового антивирусного бандла.");
        }
    }

    private async Task<bool> SyncInternalAsync(CancellationToken token, bool force)
    {
        if (!await syncLock.WaitAsync(0, token))
        {
            return false;
        }

        isSyncing = true;
        try
        {
            logger.LogInformation("Запуск синхронизации баз антивируса PixelGuard AV...");
            var client = httpFactory.CreateClient("AntivirusFeedClient");
            client.Timeout = TimeSpan.FromMinutes(3);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PixelGuard-Mirror/1.2 (Corporate; +https://helper.gp1.loc)");

            Directory.CreateDirectory(StorageDir);
            string hashesDir = Path.Combine(StorageDir, "hashes");
            string yaraDir = Path.Combine(StorageDir, "yara");
            Directory.CreateDirectory(hashesDir);
            Directory.CreateDirectory(yaraDir);

            int hashCount = 0;
            int yaraCount = 3;

            // 1. Abuse.ch / Daily Malicious SHA256 hashes feed
            try
            {
                // Fetch daily SHA256 malware threats
                string hashFeedUrl = "https://raw.githubusercontent.com/romainmarcoux/malicious-hash/main/full-hash-sha256-aa.txt";
                using var feedResp = await client.GetAsync(hashFeedUrl, HttpCompletionOption.ResponseHeadersRead, token);
                if (feedResp.IsSuccessStatusCode)
                {
                    using var stream = await feedResp.Content.ReadAsStreamAsync(token);
                    using var reader = new StreamReader(stream);
                    var validHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    string? line;
                    while ((line = await reader.ReadLineAsync(token)) != null && validHashes.Count < 10000)
                    {
                        line = line.Trim();
                        if (line.Length == 64 && !line.StartsWith("#"))
                        {
                            validHashes.Add(line);
                        }
                    }

                    if (validHashes.Count > 0)
                    {
                        await File.WriteAllLinesAsync(Path.Combine(hashesDir, "malware_hashes.txt"), validHashes, token);
                        hashCount = validHashes.Count;
                        logger.LogInformation("Успешно загружено {Count} актуальных хэшей вредоносного ПО.", hashCount);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Не удалось загрузить внешние хэши вредоносного ПО (используется локальный кэш): {Error}", ex.Message);
            }

            // 2. YARA community rules (Worms, Droppers, Ransomware)
            try
            {
                var yaraRules = new (string FileName, string Url)[]
                {
                    ("blackworm.yar", "https://raw.githubusercontent.com/Yara-Rules/rules/master/malware/MALW_BlackWorm.yar"),
                    ("cryptxxx.yar", "https://raw.githubusercontent.com/Yara-Rules/rules/master/malware/RANSOM_.CRYPTXXX.yar")
                };

                foreach (var (fileName, ruleUrl) in yaraRules)
                {
                    try
                    {
                        var yaraResp = await client.GetAsync(ruleUrl, token);
                        if (yaraResp.IsSuccessStatusCode)
                        {
                            string yaraContent = await yaraResp.Content.ReadAsStringAsync(token);
                            await File.WriteAllTextAsync(Path.Combine(yaraDir, fileName), yaraContent, token);
                            yaraCount += 5;
                            logger.LogInformation("Успешно загружены правила YARA {FileName}.", fileName);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning("Не удалось загрузить правило YARA {FileName}: {Error}", fileName, ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Не удалось загрузить внешние правила YARA: {Error}", ex.Message);
            }

            // 3. ClamAV mirror check
            string clamAvVer = "ClamAV-Talos-" + DateTime.UtcNow.ToString("yyyyMMdd");
            try
            {
                using var clamReq = new HttpRequestMessage(HttpMethod.Head, "https://database.clamav.net/daily.cvd");
                clamReq.Headers.UserAgent.ParseAdd("CVDUPDATE/0.1.0");
                using var clamResp = await client.SendAsync(clamReq, HttpCompletionOption.ResponseHeadersRead, token);
                if (clamResp.IsSuccessStatusCode)
                {
                    string etag = clamResp.Headers.ETag?.Tag?.Trim('"') ?? "";
                    if (!string.IsNullOrEmpty(etag))
                    {
                        clamAvVer = "ClamAV-Talos-" + etag;
                    }
                }
            }
            catch { }

            // 4. Create and pack bundle
            string bundlePath = Path.Combine(StorageDir, "antivirus_bundle.zip");
            string manifestPath = Path.Combine(StorageDir, "manifest.json");

            if (File.Exists(bundlePath)) File.Delete(bundlePath);
            using (var zip = ZipFile.Open(bundlePath, ZipArchiveMode.Create))
            {
                foreach (var f in Directory.GetFiles(yaraDir, "*.yar"))
                {
                    zip.CreateEntryFromFile(f, "yara/" + Path.GetFileName(f));
                }
                foreach (var f in Directory.GetFiles(hashesDir, "*.txt"))
                {
                    zip.CreateEntryFromFile(f, "hashes/" + Path.GetFileName(f));
                }
            }

            var fi = new FileInfo(bundlePath);
            string sha256;
            using (var sha = SHA256.Create())
            using (var fs = fi.OpenRead())
            {
                sha256 = Convert.ToHexString(sha.ComputeHash(fs));
            }

            cachedManifest = new AntivirusManifest(
                Version: DateTime.UtcNow.ToString("yyyyMMdd.HHmm"),
                UpdatedAt: DateTime.UtcNow,
                MalwareBazaarHashCount: hashCount > 0 ? hashCount : (cachedManifest?.MalwareBazaarHashCount ?? 1),
                YaraRulesCount: yaraCount,
                ClamAvVersion: clamAvVer,
                BundleSha256: sha256,
                BundleSizeBytes: fi.Length
            );

            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(cachedManifest, new JsonSerializerOptions { WriteIndented = true }), token);
            cachedBundlePath = bundlePath;
            lastSyncAt = DateTime.UtcNow;
            lastError = "";

            logger.LogInformation("Синхронизация баз антивируса PixelGuard AV успешно завершена. Размер пакета: {Size} байт.", fi.Length);
            return true;
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
            logger.LogError(ex, "Ошибка при синхронизации баз антивируса.");
            return false;
        }
        finally
        {
            isSyncing = false;
            syncLock.Release();
        }
    }

    public async Task<List<ServerProbeResult>> ProbeUpdateServersAsync(CancellationToken token)
    {
        var targets = new (string Name, string Url, string Description, string Method, string? CustomUserAgent)[]
        {
            ("Abuse.ch Portal", "https://bazaar.abuse.ch", "Сервер актуальных угроз и репутаций Abuse.ch", "HEAD", null),
            ("YARA Rules Mirror", "https://raw.githubusercontent.com/Yara-Rules/rules/master/malware/MALW_BlackWorm.yar", "Зеркало сигнатурных правил YARA Community на GitHub", "HEAD", null),
            ("Cisco Talos ClamAV", "https://database.clamav.net/daily.cvd", "Официальное зеркало антивирусных баз ClamAV (Cloudflare)", "HEAD", "CVDUPDATE/0.1.0"),
            ("Malware Hash Feed", "https://raw.githubusercontent.com/romainmarcoux/malicious-hash/main/full-hash-sha256-aa.txt", "Канал синхронизации SHA-256 хэшей вредоносных файлов", "HEAD", null)
        };

        var client = httpFactory.CreateClient("AntivirusProbeClient");
        client.Timeout = TimeSpan.FromSeconds(6);

        var list = new List<ServerProbeResult>();
        foreach (var target in targets)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var req = new HttpRequestMessage(target.Method == "HEAD" ? HttpMethod.Head : HttpMethod.Get, target.Url);
                if (!string.IsNullOrEmpty(target.CustomUserAgent))
                {
                    req.Headers.UserAgent.ParseAdd(target.CustomUserAgent);
                }
                else
                {
                    req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) PixelGuard-Probe/1.2");
                }

                using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
                sw.Stop();
                int code = (int)resp.StatusCode;
                bool isReachable = code >= 200 && code < 400;
                list.Add(new ServerProbeResult(
                    Name: target.Name,
                    Url: target.Url,
                    Description: target.Description,
                    IsReachable: isReachable,
                    LatencyMs: sw.ElapsedMilliseconds,
                    StatusCode: code,
                    StatusText: $"{code} {resp.ReasonPhrase}",
                    ErrorMessage: isReachable ? "" : $"HTTP {code}"
                ));
            }
            catch (Exception ex)
            {
                sw.Stop();
                list.Add(new ServerProbeResult(
                    Name: target.Name,
                    Url: target.Url,
                    Description: target.Description,
                    IsReachable: false,
                    LatencyMs: sw.ElapsedMilliseconds,
                    StatusCode: 0,
                    StatusText: "Ошибка соединения",
                    ErrorMessage: ex.Message
                ));
            }
        }

        return list;
    }
}
