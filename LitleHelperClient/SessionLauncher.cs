using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace PixelHelper;

// The SYSTEM service starts the desktop process with the signed-in user's token,
// never with its own privileged token or on the noninteractive service desktop.
internal static class SessionLauncher
{
    internal static async Task RunAsync(CancellationToken cancellation)
    {
        var started = new HashSet<string>();
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                var present = new HashSet<string>();
                if (!WTSEnumerateSessions(0, 0, 1, out var sessions, out int count)) throw new Win32Exception();
                try
                {
                    int size = Marshal.SizeOf<Session>();
                    for (int i = 0; i < count; i++)
                    {
                        var session = Marshal.PtrToStructure<Session>(sessions + i * size);
                        if (session.Id == 0 || session.State != 0 || !WTSQueryUserToken(session.Id, out var token)) continue;
                        try
                        {
                            using var identity = new WindowsIdentity(token);
                            string key = session.Id + ":" + identity.User?.Value;
                            present.Add(key);
                            if (!started.Contains(key) && Launch(token)) started.Add(key);
                        }
                        finally { CloseHandle(token); }
                    }
                }
                finally { WTSFreeMemory(sessions); }
                // Do not reopen a helper explicitly closed by the user during this session.
                started.IntersectWith(present);
            }
            catch (Exception ex) { Settings.Log(ex); }
            try { await Task.Delay(TimeSpan.FromSeconds(15), cancellation); }
            catch (OperationCanceledException) { return; }
        }
    }

    private static bool Launch(nint token)
    {
        if (!CreateEnvironmentBlock(out var environment, token, false)) return false;
        try
        {
            string executable = System.IO.Path.Combine(AppContext.BaseDirectory, "PixelHelper.exe");
            var startup = new Startup { Size = Marshal.SizeOf<Startup>(), Desktop = @"winsta0\default" };
            if (!CreateProcessAsUser(token, executable, new StringBuilder('"' + executable + '"'), 0, 0,
                false, 0x400, environment, AppContext.BaseDirectory, ref startup, out var process)) return false;
            CloseHandle(process.Thread); CloseHandle(process.Process);
            return true;
        }
        finally { DestroyEnvironmentBlock(environment); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Session { public int Id; public nint Station; public int State; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Startup
    {
        public int Size; public string? Reserved, Desktop, Title;
        public int X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
        public short Show, ReservedSize; public nint ReservedPointer, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public nint Process, Thread; public int ProcessId, ThreadId; }
    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSEnumerateSessions(nint server, int reserved, int version, out nint sessions, out int count);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(nint memory);
    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSQueryUserToken(int session, out nint token);
    [DllImport("userenv.dll", SetLastError = true)] private static extern bool CreateEnvironmentBlock(out nint environment, nint token, bool inherit);
    [DllImport("userenv.dll")] private static extern bool DestroyEnvironmentBlock(nint environment);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcessAsUser(nint token, string application, StringBuilder command, nint processAttributes, nint threadAttributes, bool inherit, int flags, nint environment, string directory, ref Startup startup, out ProcessInfo process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
