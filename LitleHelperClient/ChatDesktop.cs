using System.Runtime.InteropServices;
namespace PixelHelper;
internal static class ChatDesktop
{
    internal static bool Unlocked()
    {
        nint desktop = OpenInputDesktop(0, false, 0x0100);
        if (desktop == 0) return false;
        try { return SwitchDesktop(desktop); }
        finally { CloseDesktop(desktop); }
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool SwitchDesktop(nint desktop);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(nint desktop);
}
