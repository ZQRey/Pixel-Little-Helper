using System.IO;
using System.Media;

namespace PixelHelper;

internal enum SoundEvent
{
    MessageSent,
    MessageReceived,
    Dance,
    Urgent
}

internal static class SoundManager
{
    private static readonly Dictionary<string, SoundPlayer> players = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object sync = new();
    private static DateTime lastReceiveSound = DateTime.MinValue;

    internal static string NormalizeProfile(string? profile)
    {
        return profile switch
        {
            "VoiceAdult" => "VoiceAdult",
            "VoiceChild" => "VoiceChild",
            _ => "Sound"
        };
    }

    private static string GetEventFileName(SoundEvent ev) => ev switch
    {
        SoundEvent.MessageSent => "send.wav",
        SoundEvent.MessageReceived => "receive.wav",
        SoundEvent.Dance => "dance.wav",
        SoundEvent.Urgent => "urgent.wav",
        _ => "receive.wav"
    };

    private static string? soundBaseDir;

    internal static string GetSoundBaseDir()
    {
        if (soundBaseDir != null) return soundBaseDir;
        string direct = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds");
        if (Directory.Exists(direct)) return soundBaseDir = direct;

        string? current = AppContext.BaseDirectory;
        while (current != null)
        {
            string candidate = Path.Combine(current, "Assets", "Sounds");
            if (Directory.Exists(candidate)) return soundBaseDir = candidate;
            string candidate2 = Path.Combine(current, "LitleHelperClient", "Assets", "Sounds");
            if (Directory.Exists(candidate2)) return soundBaseDir = candidate2;
            current = Path.GetDirectoryName(current);
        }
        return soundBaseDir = direct;
    }

    private static SoundPlayer? GetPlayer(string profile, SoundEvent ev)
    {
        string normProfile = NormalizeProfile(profile);
        string eventFile = GetEventFileName(ev);
        string key = normProfile + "/" + eventFile;

        lock (sync)
        {
            if (players.TryGetValue(key, out var cached))
                return cached;

            string baseDir = GetSoundBaseDir();
            string path = Path.Combine(baseDir, normProfile, eventFile);
            if (!File.Exists(path))
            {
                // Fallback to "Sound" profile if voice file missing
                path = Path.Combine(baseDir, "Sound", eventFile);
            }

            if (File.Exists(path))
            {
                try
                {
                    var player = new SoundPlayer(path);
                    player.Load();
                    players[key] = player;
                    return player;
                }
                catch (Exception ex)
                {
                    Settings.Log(ex);
                }
            }
        }
        return null;
    }

    public static void Play(SoundEvent ev, Settings? settings = null)
    {
        if (settings != null)
        {
            if (!settings.ChatSound) return;
            if (settings.ChatDoNotDisturb && ev != SoundEvent.Urgent && !settings.ChatUrgentOverridesQuiet) return;

            if (ev == SoundEvent.MessageReceived)
            {
                if (DateTime.UtcNow - lastReceiveSound < TimeSpan.FromSeconds(1.5))
                    return;
                lastReceiveSound = DateTime.UtcNow;
            }
        }

        string profile = settings?.SoundProfile ?? "Sound";
        PlayProfile(profile, ev);
    }

    public static void PlayPreview(string profile, SoundEvent ev)
    {
        PlayProfile(profile, ev);
    }

    private static void PlayProfile(string profile, SoundEvent ev)
    {
        Task.Run(() =>
        {
            try
            {
                var player = GetPlayer(profile, ev);
                if (player != null)
                {
                    player.Play();
                }
                else
                {
                    if (ev == SoundEvent.Urgent)
                        SystemSounds.Exclamation.Play();
                    else
                        SystemSounds.Asterisk.Play();
                }
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
            }
        });
    }
}
