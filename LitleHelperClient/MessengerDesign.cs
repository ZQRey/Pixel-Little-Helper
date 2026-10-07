using System.Text.Json;
using System.Windows.Media;
namespace PixelHelper;
internal static class MessengerDesign
{
    private static readonly Lazy<JsonDocument> Tokens=new(()=>
    {
        using var source=typeof(MessengerDesign).Assembly.GetManifestResourceStream("PixelHelper.MessengerTokens")!;
        return JsonDocument.Parse(source);
    });
    internal static Brush Brush(string theme,string key)
    {
        string name=theme=="Light"?"light":theme=="Contrast"?"contrast":"dark";
        return (Brush)new BrushConverter().ConvertFromString(Tokens.Value.RootElement.GetProperty(name).GetProperty(key).GetString()!)!;
    }
}
