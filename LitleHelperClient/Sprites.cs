using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelHelper;

internal enum PetState { Idle, Sleep, Drag, Action, Greeting, Success, Error, Notice, Yawn, Wake, Busy, Dizzy, LookLeft, LookRight, LookUp, LookDown, Joy, Sad, Surprise, Laugh, Think, Celebrate, Offended, Twirl, Dance }
internal sealed class SpriteFrame
{
    internal BitmapSource Image { get; }
    internal byte[] Pixels { get; }
    internal int Width => Image.PixelWidth;
    internal int Height => Image.PixelHeight;
    internal SpriteFrame(string name)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri($"pack://application:,,,/PixelHelper;component/Assets/{name}.png");
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        Image = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        Image.Freeze();
        Pixels = new byte[Width * Height * 4];
        Image.CopyPixels(Pixels, Width * 4, 0);
    }
    internal bool Opaque(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && Pixels[(y * Width + x) * 4 + 3] >= 20;
    internal IEnumerable<Rect> Runs(double left, double top, double scale)
    {
        for (int y = 0; y < Height; y++)
        {
            int x = 0;
            while (x < Width)
            {
                while (x < Width && !Opaque(x, y)) x++;
                int start = x;
                while (x < Width && Opaque(x, y)) x++;
                if (x > start) yield return new Rect(left + start * scale, top + y * scale, (x - start) * scale, scale);
            }
        }
    }
}
internal sealed class Sprites
{
    private readonly Dictionary<PetState, SpriteFrame[]> frames = new();
    internal static int FrameCount(PetState state) => state == PetState.Idle ? 4 : state == PetState.Twirl || state == PetState.Dance ? 8 : 2;
    internal Sprites()
    {
        foreach (var state in Enum.GetValues<PetState>())
        {
            int count = FrameCount(state);
            frames[state] = Enumerable.Range(1, count).Select(i => new SpriteFrame($"{state.ToString().ToLowerInvariant()}_{i}")).ToArray();
        }
    }
    internal SpriteFrame Get(PetState state, int frame) => frames[state][frame % frames[state].Length];
}
