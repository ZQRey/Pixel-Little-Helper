using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PixelHelper;
// Native tray icon: no second UI framework or background process required.
internal sealed class TrayIcon : IDisposable
{
    private const int Callback = 0x8000 + 43;
    private readonly nint window;
    private readonly HwndSource source;
    private readonly SpriteFrame sprite;
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly Action open, menu;
    private readonly Action<int> openChat;
    private nint icon;
    private bool unread, disposed;
    private int? notificationPeer;
    private int? pendingPeer;
    internal bool Registered { get; private set; }
    internal TrayIcon(nint window, SpriteFrame sprite, Action open, Action menu, Action<int> openChat)
    {
        this.window = window; this.sprite = sprite; this.open = open; this.menu = menu; this.openChat = openChat;
        source = HwndSource.FromHwnd(window); source.AddHook(Hook); Add();
    }
    private Data Base(uint flags) => new() { Size = (uint)Marshal.SizeOf<Data>(), Window = window, Id = 1, Flags = flags, CallbackMessage = Callback, Icon = icon, Tip = unread ? "PixelHelper · Есть непрочитанные сообщения" : "PixelHelper", Info = "", Title = "" };
    private void Add()
    {
        icon = CreateIcon(sprite, unread);
        var data = Base(1 | 2 | 4);
        Registered = ShellNotifyIcon(0, ref data);
        data.Version = 4;
        ShellNotifyIcon(4, ref data);
    }
    internal void SetUnread(bool value)
    {
        if (unread == value || disposed) return; unread = value; nint old = icon; icon = CreateIcon(sprite, unread);
        var data = Base(2 | 4); ShellNotifyIcon(1, ref data); if (old != 0) DestroyIcon(old);
    }
    internal void Notify(string title, string text, int peer, bool sound)
    {
        if (disposed) return; pendingPeer = peer;
        var data = Base(0x10); data.Title = title[..Math.Min(title.Length, 63)]; data.Info = text[..Math.Min(text.Length, 255)];
        data.InfoFlags = 1u | (sound ? 0u : 0x10u); ShellNotifyIcon(1, ref data);
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (unchecked((uint)message) == taskbarCreated && !disposed) { if (icon != 0) DestroyIcon(icon); Add(); }
        if (message == Callback && !disposed)
        {
            handled = true; int action = (int)(lParam.ToInt64() & 0xffff);
            if (action is 0x400 or 0x401 or 0x203) open();
            else if (action is 0x7b or 0x205) menu();
            else if (action == 0x402) notificationPeer = pendingPeer;
            else if (action is 0x403 or 0x404) notificationPeer = null;
            else if (action == 0x405) { if ((notificationPeer ?? pendingPeer) is int peer) openChat(peer); else open(); notificationPeer = null; }
        }
        return 0;
    }
    internal static nint CreateIcon(SpriteFrame sprite, bool unread)
    {
        const int size = 32; using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(40); writer.Write(size); writer.Write(size * 2); writer.Write((short)1); writer.Write((short)32); writer.Write(0); writer.Write(size * size * 4); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        for (int y = size - 1; y >= 0; y--) for (int x = 0; x < size; x++)
        {
            int offset = ((y * sprite.Height / size) * sprite.Width + x * sprite.Width / size) * 4;
            bool badge = unread && (x - 25) * (x - 25) + (y - 6) * (y - 6) <= 36;
            writer.Write(badge ? (byte)30 : sprite.Pixels[offset]); writer.Write(badge ? (byte)110 : sprite.Pixels[offset + 1]); writer.Write(badge ? (byte)255 : sprite.Pixels[offset + 2]); writer.Write(badge ? (byte)255 : sprite.Pixels[offset + 3]);
        }
        writer.Write(new byte[size * size / 8]); var bytes = stream.ToArray();
        return CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, 0x30000, size, size, 0);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; var data = Base(0); ShellNotifyIcon(2, ref data); source.RemoveHook(Hook); if (icon != 0) DestroyIcon(icon); icon = 0;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Data
    {
        public uint Size; public nint Window; public uint Id, Flags, CallbackMessage; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ShellNotifyIcon(uint operation, ref Data data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern nint CreateIconFromResourceEx(byte[] data, uint size, [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(nint icon);
}
