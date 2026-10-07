using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelHelper;
internal record HelperEmoji(string Code, string Name, PetState State, double Seconds = 2);
internal static class HelperEmojis
{
    internal static readonly HelperEmoji[] All = [
        new(":helper_wave:", "Привет", PetState.Greeting), new(":helper_joy:", "Радость", PetState.Joy),
        new(":helper_thanks:", "Спасибо", PetState.Success), new(":helper_sad:", "Грусть", PetState.Sad),
        new(":helper_surprise:", "Удивление", PetState.Surprise), new(":helper_laugh:", "Смех", PetState.Laugh),
        new(":helper_sleep:", "Сон", PetState.Sleep), new(":helper_think:", "Думаю", PetState.Think),
        new(":helper_party:", "Праздник", PetState.Celebrate), new(":helper_dizzy:", "Головокружение", PetState.Dizzy, 3)
    ];
    private static readonly Dictionary<string, HelperEmoji> codes = All.ToDictionary(e => e.Code);
    private static readonly Regex token = new(@":helper_[a-z_]{1,24}:", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Lazy<Sprites> sprites = new(() => new Sprites());
    internal static IEnumerable<HelperEmoji> Parse(string text) => token.Matches(text).Select(m => codes.GetValueOrDefault(m.Value)).OfType<HelperEmoji>();
    internal static BitmapSource Image(HelperEmoji emoji) => sprites.Value.Get(emoji.State, 0).Image;
    internal static string PlainText(string text) => token.Replace(text, m => codes.TryGetValue(m.Value, out var emoji) ? "[" + emoji.Name + "]" : m.Value);
    internal static Image AnimatedImage(HelperEmoji emoji, double size, bool animated = true)
    {
        var image = new Image { Source = Image(emoji), Width = size, Height = size, ToolTip = emoji.Name, Margin = new Thickness(2, 0, 2, 0) };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) }; int frame = 0;
        timer.Tick += (_, _) =>
        {
            var window = Window.GetWindow(image);
            if (window?.WindowState == WindowState.Minimized || !image.IsVisible) return;
            var viewer = FindViewer(image);
            if (viewer != null) { var bounds = image.TransformToAncestor(viewer).TransformBounds(new Rect(image.RenderSize)); if (!bounds.IntersectsWith(new Rect(viewer.RenderSize))) return; }
            image.Source = sprites.Value.Get(emoji.State, ++frame).Image;
        };
        image.Loaded += (_, _) => { if (animated) timer.Start(); };
        image.Unloaded += (_, _) => timer.Stop();
        return image;
    }
    private static ScrollViewer? FindViewer(DependencyObject child)
    {
        for (var parent = VisualTreeHelper.GetParent(child); parent != null; parent = VisualTreeHelper.GetParent(parent)) if (parent is ScrollViewer viewer) return viewer;
        return null;
    }
    internal static TextBlock Render(string text, double size = 32, bool animated = true)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14 }; int offset = 0;
        foreach (Match match in token.Matches(text))
        {
            if (!codes.TryGetValue(match.Value, out var emoji)) continue;
            block.Inlines.Add(new Run(text[offset..match.Index]));
            var image = AnimatedImage(emoji, size, animated);
            block.Inlines.Add(new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center }); offset = match.Index + match.Length;
        }
        block.Inlines.Add(new Run(text[offset..])); return block;
    }
}
internal sealed class EmojiReactions
{
    private readonly Queue<(HelperEmoji Emoji, DateTime Expires)> pending = new();
    private readonly Queue<string> seen = new();
    private readonly HashSet<string> keys = new();
    private readonly string? file;
    private string context = "";
    internal int Count => pending.Count;
    internal EmojiReactions(string? file = null) { this.file = file; }
    internal void Clear() => pending.Clear();
    internal void Insert(HelperEmoji emoji, DateTime? now = null)
    { if (pending.Count < 9) pending.Enqueue((emoji, (now ?? DateTime.UtcNow).AddSeconds(30))); }
    internal bool Incoming(string identity, ChatEntry message, bool enabled)
    {
        var emojis = HelperEmojis.Parse(message.Body).Take(3).ToArray(); if (emojis.Length == 0) return false;
        if (context != identity)
        {
            context = identity; Clear(); seen.Clear(); keys.Clear();
            try { if (file != null && File.Exists(file)) { var data = JsonSerializer.Deserialize<Seen>(File.ReadAllText(file)); if (data?.Context == context) foreach (string key in data.Keys.TakeLast(256)) if (keys.Add(key)) seen.Enqueue(key); } }
            catch (Exception ex) { Settings.Log(ex); }
        }
        string id = message.SenderId + ":" + message.RecipientId + ":" + message.ClientId;
        if (!keys.Add(id)) return false; seen.Enqueue(id); while (seen.Count > 256) keys.Remove(seen.Dequeue());
        try { if (file != null) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(new Seen(context, seen.ToArray()))); File.Move(file + ".tmp", file, true); } }
        catch (Exception ex) { Settings.Log(ex); }
        if (!enabled) return false;
        foreach (var emoji in emojis) Insert(emoji); return true;
    }
    internal HelperEmoji? Take(DateTime now)
    {
        while (pending.TryDequeue(out var item)) if (item.Expires > now) return item.Emoji;
        return null;
    }
    private record Seen(string Context, string[] Keys);
}
