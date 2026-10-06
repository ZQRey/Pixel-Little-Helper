using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PixelHelper;

internal static class NativeMethods
{
    internal const int WM_NCHITTEST = 0x84, WM_WINDOWPOSCHANGING = 0x46, WM_DPICHANGED = 0x2E0;
    internal static readonly nint HWND_BOTTOM = new(1);
    internal const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10;
    private const int GWL_EXSTYLE = -20;
    internal const long WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    [DllImport("user32.dll", SetLastError = true)] private static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLongW(nint hwnd, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLongW(nint hwnd, int index, int value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(POINT point);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    private static nint desktopHost;
    internal static int BottomChanges { get; private set; }
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string? className, string? title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    private delegate bool EnumWindowCallback(nint hwnd, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, nint parameter);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint dest, nint source1, nint source2, int mode);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct WINDOWPOS { public nint Hwnd, HwndInsertAfter; public int X, Y, Cx, Cy; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct LASTINPUTINFO { public uint Size, Time; }
    internal static TimeSpan IdleTime()
    {
        var info = new LASTINPUTINFO { Size = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time)) : TimeSpan.Zero;
    }
    internal static void ToolWindow(nint hwnd, bool noActivate)
    {
        long style = IntPtr.Size == 8 ? GetWindowLongPtrW(hwnd, GWL_EXSTYLE).ToInt64() : GetWindowLongW(hwnd, GWL_EXSTYLE);
        style |= WS_EX_TOOLWINDOW;
        if (noActivate) style |= WS_EX_NOACTIVATE;
        Marshal.SetLastPInvokeError(0);
        var previous = IntPtr.Size == 8 ? SetWindowLongPtrW(hwnd, GWL_EXSTYLE, new nint(style)) : new nint(SetWindowLongW(hwnd, GWL_EXSTYLE, (int)style));
        if (previous == 0 && Marshal.GetLastPInvokeError() != 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
    internal static nint DesktopAnchor(nint hwnd)
    {
        if (desktopHost == 0 || !IsWindow(desktopHost))
        {
            desktopHost = GetShellWindow();
            EnumWindows((candidate, _) =>
            {
                if (FindWindowEx(candidate, 0, "SHELLDLL_DefView", null) == 0) return true;
                desktopHost = candidate; return false;
            }, 0);
        }
        nint desktop = desktopHost;
        if (desktop == 0) return HWND_BOTTOM;
        nint preceding = GetWindow(desktop, 3); // GW_HWNDPREV: immediately above the icon desktop.
        for (int guard = 0; preceding != 0 && guard < 32; guard++)
        {
            GetWindowThreadProcessId(preceding, out uint pid);
            if (preceding != hwnd && pid != Environment.ProcessId) break;
            preceding = GetWindow(preceding, 3);
        }
        return preceding; // HWND_TOP only when no other windows precede the desktop.
    }
    internal static void Bottom(nint hwnd)
    {
        nint anchor = DesktopAnchor(hwnd);
        if (GetWindow(hwnd, 3) == anchor) return;
        BottomChanges++;
        // Literal HWND_BOTTOM can hide a pet beneath Explorer's full-screen desktop.
        // First clear topmost status, then place just above the icon host, below ordinary apps.
        const uint noSendChanging = 0x0400;
        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | noSendChanging);
        SetWindowPos(hwnd, anchor, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | noSendChanging);
    }
    // A real region is required: HTTRANSPARENT alone forwards only within the same UI thread.
    internal static void SetRegion(nint hwnd, IEnumerable<Rect> rectangles, double sx, double sy)
    {
        nint result = BuildRegion(rectangles, sx, sy);
        ApplyRegion(hwnd, result);
    }
    internal static nint BuildRegion(IEnumerable<Rect> rectangles, double sx, double sy)
    {
        nint result = CreateRectRgn(0, 0, 0, 0);
        if (result == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        try
        {
            foreach (var rect in rectangles)
            {
                nint part = CreateRectRgn((int)Math.Floor(rect.Left * sx), (int)Math.Floor(rect.Top * sy),
                    (int)Math.Ceiling(rect.Right * sx), (int)Math.Ceiling(rect.Bottom * sy));
                if (part == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                try { if (CombineRgn(result, result, part, 2) == 0) throw new Win32Exception(); }
                finally { DeleteObject(part); }
            }
            var saved = result; result = 0; return saved;
        }
        finally { if (result != 0) DeleteObject(result); }
    }
    internal static void ApplyRegion(nint hwnd, nint cached)
    {
        var copy = CreateRectRgn(0, 0, 0, 0);
        if (copy == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (CombineRgn(copy, cached, 0, 5) == 0 || SetWindowRgn(hwnd, copy, false) == 0)
        {
            DeleteObject(copy); throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        // Windows owns the copied region. Keep the cached original for subsequent frames.
    }
    internal static void FreeRegion(nint region) => DeleteObject(region);
    internal static Rect DesktopBounds()
    {
        return new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
    }
}
