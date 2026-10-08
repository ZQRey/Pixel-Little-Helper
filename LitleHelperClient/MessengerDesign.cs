using System.Text.Json;
using System.Windows.Media;

namespace PixelHelper;

internal static class MessengerDesign
{
    private static readonly Lazy<JsonDocument> Tokens = new(() =>
    {
        using var source = typeof(MessengerDesign).Assembly.GetManifestResourceStream("PixelHelper.MessengerTokens")!;
        return JsonDocument.Parse(source);
    });

    internal static Brush Brush(string theme, string key)
    {
        string name = theme == "Light" ? "light" : theme == "Contrast" ? "contrast" : "dark";
        if (Tokens.Value.RootElement.GetProperty(name).TryGetProperty(key, out var prop))
            return (Brush)new BrushConverter().ConvertFromString(prop.GetString()!)!;

        return key switch
        {
            "line" => (Brush)new BrushConverter().ConvertFromString(theme == "Light" ? "#E2E8F0" : theme == "Contrast" ? "#FFFFFF" : "#1E293B")!,
            "card" => (Brush)new BrushConverter().ConvertFromString(theme == "Light" ? "#F8FAFC" : theme == "Contrast" ? "#000000" : "#141C2E")!,
            "hover" => (Brush)new BrushConverter().ConvertFromString(theme == "Light" ? "#EEF2FF" : "#1A2238")!,
            "selected" => (Brush)new BrushConverter().ConvertFromString(theme == "Light" ? "#E0E7FF" : "#283056")!,
            _ => Brushes.Transparent
        };
    }

    internal static LinearGradientBrush Gradient => new(
        Color.FromRgb(99, 102, 241),
        Color.FromRgb(139, 92, 246),
        45);
}
