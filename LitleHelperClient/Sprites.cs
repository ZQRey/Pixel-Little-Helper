using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelHelper;

internal enum PetState { Idle, Sleep, Drag, Action, Greeting, Success, Error, Notice, Yawn, Wake, Busy, Dizzy, LookLeft, LookRight, LookUp, LookDown, Joy, Sad, Surprise, Laugh, Think, Celebrate, Offended, Twirl, Dance, Workout, Charging, Facepalm, Flower, Cry, TurnBack, Shy, PetCat, PetDog }
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
internal enum FaceNoticeStatus { None, Connected, Disconnected, Heart, Mail }
internal sealed class FaceBadges
{
    private readonly BitmapSource connected;
    private readonly BitmapSource disconnected;
    private readonly BitmapSource mail;
    private readonly BitmapSource[] unreadDigits;
    private readonly BitmapSource unreadPlus;
    private readonly BitmapSource[] heartFrames;

    internal FaceBadges()
    {
        connected = new SpriteFrame("face_connected").Image;
        disconnected = new SpriteFrame("face_disconnected").Image;
        mail = new SpriteFrame("face_mail").Image;
        unreadDigits = Enumerable.Range(1, 9).Select(i => new SpriteFrame($"face_unread_{i}").Image).ToArray();
        unreadPlus = new SpriteFrame("face_unread_plus").Image;
        heartFrames = Enumerable.Range(1, 4).Select(i => new SpriteFrame($"face_heart_{i}").Image).ToArray();
    }

    internal BitmapSource? Get(FaceNoticeStatus status, int unreadCount, int heartPhase = 0)
    {
        if (status == FaceNoticeStatus.Connected) return connected;
        if (status == FaceNoticeStatus.Disconnected) return disconnected;
        if (status == FaceNoticeStatus.Mail) return mail;
        if (status == FaceNoticeStatus.Heart) return heartFrames[heartPhase % heartFrames.Length];
        if (unreadCount <= 0) return null;
        return unreadCount <= 9 ? unreadDigits[unreadCount - 1] : unreadPlus;
    }
}
internal sealed class Sprites
{
    private readonly Dictionary<PetState, SpriteFrame[]> frames = new();
    internal static int FrameCount(PetState state) =>
        state == PetState.Idle || state == PetState.Charging ? 4 :
        state is PetState.Twirl or PetState.Dance or PetState.Workout ? 8 : 2;
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

