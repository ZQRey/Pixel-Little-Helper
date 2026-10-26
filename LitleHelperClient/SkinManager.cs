using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelHelper;

public sealed class SkinManager : ISkinManager
{
    private static readonly Lazy<SkinManager> instance = new(() => new SkinManager());
    public static SkinManager Instance => instance.Value;

    private readonly object syncLock = new();
    private readonly Dictionary<string, SkinInfo> availableSkins = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BitmapSource> bitmapCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SpriteFrame> spriteFrameCache = new(StringComparer.OrdinalIgnoreCase);

    private Settings? settings;
    private SkinInfo currentSkin = null!;
    private string currentSkinId = "Classic";

    public SkinInfo CurrentSkin
    {
        get
        {
            lock (syncLock) return currentSkin;
        }
    }

    public string CurrentSkinId
    {
        get
        {
            lock (syncLock) return currentSkinId;
        }
    }

    public event EventHandler<SkinChangedEventArgs>? SkinChanged;

    public SkinManager()
    {
        DiscoverSkins();
        EnsureDefaultSkins();

        // Default to Classic
        if (availableSkins.TryGetValue("Classic", out var classic))
        {
            currentSkin = classic;
            currentSkinId = "Classic";
        }
        else
        {
            var fallbackManifest = new SkinManifest { Id = "Classic", DisplayName = "Классический" };
            currentSkin = new SkinInfo("Classic", "Классический", "PixelHelper", "1.0", fallbackManifest);
            availableSkins["Classic"] = currentSkin;
            currentSkinId = "Classic";
        }

        // Warm up Classic skin cache
        PreloadSkin("Classic");
    }

    public void Initialize(Settings userSettings)
    {
        lock (syncLock)
        {
            settings = userSettings;
            string requestedId = string.IsNullOrWhiteSpace(userSettings.SelectedSkinId) ? "Classic" : userSettings.SelectedSkinId;
            if (availableSkins.ContainsKey(requestedId) && !string.Equals(requestedId, currentSkinId, StringComparison.OrdinalIgnoreCase))
            {
                SetSkinInternal(requestedId, saveToSettings: false);
            }
        }
    }

    public IReadOnlyList<SkinInfo> GetAvailableSkins()
    {
        lock (syncLock)
        {
            return availableSkins.Values.OrderBy(s => s.Id == "Classic" ? 0 : 1).ThenBy(s => s.DisplayName).ToList();
        }
    }

    public bool SetSkin(string skinId)
    {
        if (string.IsNullOrWhiteSpace(skinId)) return false;
        return SetSkinInternal(skinId, saveToSettings: true);
    }

    private bool SetSkinInternal(string skinId, bool saveToSettings)
    {
        SkinInfo newSkin;
        string previousSkinId;
        lock (syncLock)
        {
            if (!availableSkins.TryGetValue(skinId, out var found)) return false;
            if (string.Equals(currentSkinId, skinId, StringComparison.OrdinalIgnoreCase)) return true;

            previousSkinId = currentSkinId;
            currentSkinId = skinId;
            currentSkin = found;
            newSkin = found;

            // Preload new skin frames to eliminate any frame drops during animation ticks
            PreloadSkin(skinId);

            if (saveToSettings && settings != null)
            {
                settings.SelectedSkinId = skinId;
                Task.Run(() =>
                {
                    try { settings.Save(); }
                    catch (Exception ex) { Settings.Log(ex); }
                });
            }
        }

        SkinChanged?.Invoke(this, new SkinChangedEventArgs(previousSkinId, newSkin.Id, newSkin.Manifest));
        return true;
    }

    public BitmapSource GetFrame(PetState state, int frameIndex)
    {
        int count = Sprites.FrameCount(state);
        int frameNumber = (frameIndex % count) + 1;
        string stateName = state.ToString().ToLowerInvariant();
        string activeSkinId = CurrentSkinId;

        // Level 1: Active Skin
        string activeKey = $"{activeSkinId}_{stateName}_{frameNumber}";
        if (bitmapCache.TryGetValue(activeKey, out var frame)) return frame;

        var loaded = TryLoadBitmap(activeSkinId, stateName, frameNumber);
        if (loaded != null)
        {
            bitmapCache[activeKey] = loaded;
            return loaded;
        }

        // Level 2: Fallback to Classic
        if (!string.Equals(activeSkinId, "Classic", StringComparison.OrdinalIgnoreCase))
        {
            string classicKey = $"Classic_{stateName}_{frameNumber}";
            if (bitmapCache.TryGetValue(classicKey, out var classicFrame)) return classicFrame;

            var classicLoaded = TryLoadBitmap("Classic", stateName, frameNumber);
            if (classicLoaded != null)
            {
                bitmapCache[classicKey] = classicLoaded;
                return classicLoaded;
            }
        }

        // Level 3: Fallback to Classic_idle_1
        string fallbackKey = "Classic_idle_1";
        if (bitmapCache.TryGetValue(fallbackKey, out var fallbackFrame)) return fallbackFrame;

        var idleLoaded = TryLoadBitmap("Classic", "idle", 1);
        if (idleLoaded != null)
        {
            bitmapCache[fallbackKey] = idleLoaded;
            return idleLoaded;
        }

        return GetEmptyFallback();
    }

    public SpriteFrame GetSpriteFrame(PetState state, int frameIndex)
    {
        int count = Sprites.FrameCount(state);
        int frameNumber = (frameIndex % count) + 1;
        string stateName = state.ToString().ToLowerInvariant();
        string activeSkinId = CurrentSkinId;

        string activeKey = $"{activeSkinId}_{stateName}_{frameNumber}";
        if (spriteFrameCache.TryGetValue(activeKey, out var sf)) return sf;

        var bitmap = GetFrame(state, frameIndex);
        var created = new SpriteFrame(bitmap);
        spriteFrameCache[activeKey] = created;
        return created;
    }

    private void PreloadSkin(string skinId)
    {
        foreach (var state in Enum.GetValues<PetState>())
        {
            int count = Sprites.FrameCount(state);
            string stateName = state.ToString().ToLowerInvariant();
            for (int f = 1; f <= count; f++)
            {
                string key = $"{skinId}_{stateName}_{f}";
                if (!bitmapCache.ContainsKey(key))
                {
                    var bmp = TryLoadBitmap(skinId, stateName, f);
                    if (bmp != null)
                    {
                        bitmapCache[key] = bmp;
                        if (!spriteFrameCache.ContainsKey(key))
                        {
                            spriteFrameCache[key] = new SpriteFrame(bmp);
                        }
                    }
                }
            }
        }
    }

    private static BitmapSource? TryLoadBitmap(string skinId, string stateName, int frameNumber)
    {
        // 1. Pack URI: Assets/Skins/{skinId}/{stateName}_{frameNumber}.png
        try
        {
            var uri = new Uri($"pack://application:,,,/PixelHelper;component/Assets/Skins/{skinId}/{stateName}_{frameNumber}.png");
            var streamInfo = Application.GetResourceStream(uri);
            if (streamInfo != null)
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = streamInfo.Stream;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                var converted = new FormatConvertedBitmap(bi, PixelFormats.Bgra32, null, 0);
                converted.Freeze();
                return converted;
            }
        }
        catch { }

        // 2. If Classic, check root Assets/{stateName}_{frameNumber}.png for backward compatibility
        if (string.Equals(skinId, "Classic", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/PixelHelper;component/Assets/{stateName}_{frameNumber}.png");
                var streamInfo = Application.GetResourceStream(uri);
                if (streamInfo != null)
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.StreamSource = streamInfo.Stream;
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    var converted = new FormatConvertedBitmap(bi, PixelFormats.Bgra32, null, 0);
                    converted.Freeze();
                    return converted;
                }
            }
            catch { }
        }

        // 3. Filesystem check
        string[] searchDirs =
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "Skins", skinId),
            Path.Combine(Environment.CurrentDirectory, "Assets", "Skins", skinId),
            Path.Combine(AppContext.BaseDirectory, "Assets"),
            Path.Combine(Environment.CurrentDirectory, "Assets")
        };

        foreach (var dir in searchDirs)
        {
            string file = Path.Combine(dir, $"{stateName}_{frameNumber}.png");
            if (File.Exists(file))
            {
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(file, UriKind.Absolute);
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    var converted = new FormatConvertedBitmap(bi, PixelFormats.Bgra32, null, 0);
                    converted.Freeze();
                    return converted;
                }
                catch { }
            }
        }

        return null;
    }

    private void DiscoverSkins()
    {
        // Scan filesystem for any dynamic custom skins
        string[] searchRoots =
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "Skins"),
            Path.Combine(Environment.CurrentDirectory, "Assets", "Skins")
        };

        foreach (var root in searchRoots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var dir in Directory.GetDirectories(root))
                {
                    string jsonPath = Path.Combine(dir, "skin.json");
                    if (File.Exists(jsonPath))
                    {
                        try
                        {
                            string json = File.ReadAllText(jsonPath);
                            var manifest = JsonSerializer.Deserialize<SkinManifest>(json, Settings.Json);
                            if (manifest != null && !string.IsNullOrWhiteSpace(manifest.Id))
                            {
                                availableSkins[manifest.Id] = new SkinInfo(manifest.Id, manifest.DisplayName, manifest.Author, manifest.Version, manifest);
                            }
                        }
                        catch (Exception ex) { Settings.Log(ex); }
                    }
                }
            }
            catch (Exception ex) { Settings.Log(ex); }
        }
    }

    private void EnsureDefaultSkins()
    {
        // Ensure Classic
        if (!availableSkins.ContainsKey("Classic"))
        {
            var manifest = LoadManifestFromResource("Classic") ?? new SkinManifest
            {
                Id = "Classic",
                DisplayName = "Классический",
                Author = "PixelHelper",
                Version = "1.0",
                BadgeAnchor = new SkinAnchor { X = 16, Y = 12 },
                HatAnchor = new SkinAnchor { X = 8, Y = -4 }
            };
            availableSkins["Classic"] = new SkinInfo(manifest.Id, manifest.DisplayName, manifest.Author, manifest.Version, manifest);
        }

        // Ensure StealthNeon
        if (!availableSkins.ContainsKey("StealthNeon"))
        {
            var manifest = LoadManifestFromResource("StealthNeon") ?? new SkinManifest
            {
                Id = "StealthNeon",
                DisplayName = "Тёмный неон (Stealth Neon)",
                Author = "PixelHelper",
                Version = "1.0",
                ScalingMode = "NearestNeighbor",
                BadgeAnchor = new SkinAnchor { X = 16, Y = 12 },
                HatAnchor = new SkinAnchor { X = 8, Y = -4 }
            };
            availableSkins["StealthNeon"] = new SkinInfo(manifest.Id, manifest.DisplayName, manifest.Author, manifest.Version, manifest);
        }

        // Ensure CyberFamiliar
        if (!availableSkins.ContainsKey("CyberFamiliar"))
        {
            var manifest = LoadManifestFromResource("CyberFamiliar") ?? new SkinManifest
            {
                Id = "CyberFamiliar",
                DisplayName = "Кибер-дух (Cyber Familiar)",
                Author = "PixelHelper",
                Version = "1.0",
                SpriteWidth = 96,
                SpriteHeight = 96,
                ScalingMode = "HighQuality",
                BadgeAnchor = new SkinAnchor { X = 16, Y = 14 },
                HatAnchor = new SkinAnchor { X = 8, Y = -6 }
            };
            availableSkins["CyberFamiliar"] = new SkinInfo(manifest.Id, manifest.DisplayName, manifest.Author, manifest.Version, manifest);
        }

        // Ensure GlassmorphismOrb
        if (!availableSkins.ContainsKey("GlassmorphismOrb"))
        {
            var manifest = LoadManifestFromResource("GlassmorphismOrb") ?? new SkinManifest
            {
                Id = "GlassmorphismOrb",
                DisplayName = "Голографическая сфера (Glassmorphism Orb)",
                Author = "PixelHelper",
                Version = "1.0",
                SpriteWidth = 96,
                SpriteHeight = 96,
                ScalingMode = "HighQuality",
                BadgeAnchor = new SkinAnchor { X = 16, Y = 14 },
                HatAnchor = new SkinAnchor { X = 8, Y = -6 }
            };
            availableSkins["GlassmorphismOrb"] = new SkinInfo(manifest.Id, manifest.DisplayName, manifest.Author, manifest.Version, manifest);
        }

        // Ensure Mage (Arcane Mage)
        if (!availableSkins.ContainsKey("Mage"))
        {
            var manifest = LoadManifestFromResource("Mage") ?? new SkinManifest
            {
                Id = "Mage",
                DisplayName = "Энергичный чародей (Arcane Mage)",
                Author = "PixelHelper",
                Version = "1.0",
                SpriteWidth = 96,
                SpriteHeight = 96,
                ScalingMode = "HighQuality",
                BadgeAnchor = new SkinAnchor { X = 35.0, Y = 12.0 },
                HatAnchor = new SkinAnchor { X = 24.0, Y = -6.0 },
                FrameIntervalsMs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Twirl"] = 100,
                    ["Sleep"] = 300,
                    ["Workout"] = 150,
                    ["Dance"] = 120,
                    ["Charging"] = 150,
                    ["Idle"] = 140,
                    ["Default"] = 120
                }
            };
            availableSkins["Mage"] = new SkinInfo(manifest.Id, manifest.DisplayName, manifest.Author, manifest.Version, manifest);
        }
    }

    private static SkinManifest? LoadManifestFromResource(string skinId)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/PixelHelper;component/Assets/Skins/{skinId}/skin.json");
            var streamInfo = Application.GetResourceStream(uri);
            if (streamInfo != null)
            {
                using var reader = new StreamReader(streamInfo.Stream);
                string json = reader.ReadToEnd();
                return JsonSerializer.Deserialize<SkinManifest>(json, Settings.Json);
            }
        }
        catch { }
        return null;
    }

    private static BitmapSource GetEmptyFallback()
    {
        var bmp = BitmapSource.Create(48, 48, 96, 96, PixelFormats.Bgra32, null, new byte[48 * 48 * 4], 48 * 4);
        bmp.Freeze();
        return bmp;
    }
}
