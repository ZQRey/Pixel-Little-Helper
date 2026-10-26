using System.Text.Json.Serialization;

namespace PixelHelper;

public sealed class SkinAnchor
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

public sealed class SkinManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "Classic";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "Классический";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "PixelHelper";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("spriteWidth")]
    public int SpriteWidth { get; set; } = 48;

    [JsonPropertyName("spriteHeight")]
    public int SpriteHeight { get; set; } = 48;

    [JsonPropertyName("badgeAnchor")]
    public SkinAnchor BadgeAnchor { get; set; } = new() { X = 16, Y = 12 };

    [JsonPropertyName("hatAnchor")]
    public SkinAnchor HatAnchor { get; set; } = new() { X = 8, Y = -4 };

    [JsonPropertyName("scalingMode")]
    public string ScalingMode { get; set; } = "NearestNeighbor";

    [JsonPropertyName("frameIntervalsMs")]
    public Dictionary<string, int> FrameIntervalsMs { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Twirl"] = 150,
        ["Sleep"] = 250,
        ["Offended"] = 450,
        ["Default"] = 100
    };

    public int GetIntervalMs(PetState state)
    {
        if (FrameIntervalsMs.TryGetValue(state.ToString(), out int ms)) return ms;
        if (FrameIntervalsMs.TryGetValue("Default", out int defMs)) return defMs;
        return 100;
    }
}
