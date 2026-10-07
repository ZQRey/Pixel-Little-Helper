using System.Runtime.InteropServices;
using System.Text;

namespace PixelHelper;

internal static class UserGreeting
{
    internal static string Text()
    {
        try
        {
            // NameDisplay is the AD display name, rather than sAMAccountName/UPN.
            uint size = 0;
            GetUserNameEx(3, null, ref size);
            if (size is > 0 and < 4096)
            {
                var name = new StringBuilder((int)size);
                if (GetUserNameEx(3, name, ref size) && !string.IsNullOrWhiteSpace(name.ToString()))
                    return "Здравствуйте, " + name.ToString().Trim() + "! Я ваш помощник.";
            }
            if (NetUserGetInfo(null, Environment.UserName, 10, out var buffer) == 0)
            {
                try
                {
                    var user = Marshal.PtrToStructure<UserInfo>(buffer);
                    if (!string.IsNullOrWhiteSpace(user.FullName)) return "Здравствуйте, " + user.FullName.Trim() + "! Я ваш помощник.";
                }
                finally { NetApiBufferFree(buffer); }
            }
        }
        catch (Exception ex) { Settings.Log(ex); }
        // Offline/domain lookup failure must not greet an AD user by their login.
        return "Здравствуйте! Я ваш помощник.";
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct UserInfo { public string? Name, Comment, UserComment, FullName; }
    [DllImport("secur32.dll", CharSet = CharSet.Unicode)] private static extern bool GetUserNameEx(int format, StringBuilder? name, ref uint size);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] private static extern int NetUserGetInfo(string? server, string user, int level, out nint buffer);
    [DllImport("netapi32.dll")] private static extern int NetApiBufferFree(nint buffer);
}
