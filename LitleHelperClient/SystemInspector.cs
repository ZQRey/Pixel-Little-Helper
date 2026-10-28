using Microsoft.Win32;
using System.Management;
using System.Net;
using System.Net.Sockets;

namespace PixelHelper;

public static class SystemInspector
{
    [System.Runtime.InteropServices.DllImport("secur32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern int GetUserNameEx(int nameFormat, System.Text.StringBuilder userName, ref uint userNameSize);

    public static bool IsTemporaryUser(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return true;
        string clean = username.Trim().Split('\\').Last().Split('@')[0].ToLowerInvariant();
        return clean is "temp" or "tmp" or "system" or "local service" or "network service" or "defaultuser0"
            || clean.StartsWith("temp.") || clean.StartsWith("defaultuser");
    }

    public static bool IsTemporaryProfile()
    {
        if (IsTemporaryUser(Environment.UserName)) return true;
        try
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (profile.EndsWith(@"\TEMP", StringComparison.OrdinalIgnoreCase) || profile.Contains(@"\TEMP.", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch { }
        return false;
    }

    public static string GetLastLoggedOnUser()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI");
            string? sam = key?.GetValue("LastLoggedOnSAMUser") as string;
            if (!string.IsNullOrWhiteSpace(sam))
            {
                string clean = sam.Trim().Split('\\').Last().Split('@')[0];
                if (!IsTemporaryUser(clean)) return clean;
            }
            string? user = key?.GetValue("LastLoggedOnUser") as string;
            if (!string.IsNullOrWhiteSpace(user))
            {
                string clean = user.Trim().Split('\\').Last().Split('@')[0];
                if (!IsTemporaryUser(clean)) return clean;
            }
        }
        catch { }
        return "";
    }

    public static string GetLastLoggedOnDisplayName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI");
            string? display = key?.GetValue("LastLoggedOnDisplayName") as string;
            if (!string.IsNullOrWhiteSpace(display) && !IsTemporaryUser(display))
                return display.Trim();
        }
        catch { }
        return "";
    }

    public static string GetUserFullName()
    {
        if (!IsTemporaryUser(Environment.UserName))
        {
            try
            {
                var sb = new System.Text.StringBuilder(260);
                uint size = (uint)sb.Capacity;
                if (GetUserNameEx(3, sb, ref size) != 0 && sb.Length > 0)
                {
                    string full = sb.ToString().Trim();
                    if (!string.IsNullOrEmpty(full) && !IsTemporaryUser(full)) return full;
                }
            }
            catch { }

            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT FullName FROM Win32_UserAccount WHERE Name = '{Environment.UserName}'");
                foreach (ManagementObject user in searcher.Get())
                {
                    string? fn = user["FullName"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(fn) && !IsTemporaryUser(fn)) return fn;
                }
            }
            catch { }
        }

        string logonDisplay = GetLastLoggedOnDisplayName();
        if (!string.IsNullOrEmpty(logonDisplay)) return logonDisplay;

        string lastUser = GetLastLoggedOnUser();
        if (!string.IsNullOrEmpty(lastUser))
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT FullName FROM Win32_UserAccount WHERE Name = '{lastUser}'");
                foreach (ManagementObject user in searcher.Get())
                {
                    string? fn = user["FullName"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(fn) && !IsTemporaryUser(fn)) return fn;
                }
            }
            catch { }
            return lastUser;
        }

        return Environment.UserName;
    }

    public static string ClientVersion()
    {
        var ver = typeof(SystemInspector).Assembly.GetName().Version;
        return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.2.79";
    }

    public static MachineInfo Machine()
    {
        string ip = "";
        try { ip = Dns.GetHostAddresses(Environment.MachineName).FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x))?.ToString() ?? ""; }
        catch (SocketException) { }

        string user = Environment.UserName;
        if (IsTemporaryUser(user))
        {
            string recovered = GetLastLoggedOnUser();
            if (!string.IsNullOrEmpty(recovered)) user = recovered;
        }

        return new(Environment.MachineName, user, Environment.UserDomainName, ip, Environment.OSVersion.VersionString, GetUserFullName(), ClientVersion: ClientVersion());
    }
    public static InventorySnapshot Collect()
    {
        List<string> errors = [];
        List<MemorySlot> ram = []; List<LogicalDisk> logical = []; List<PhysicalDisk> physical = [];
        string cpu = "";
        void Query(string text, Action<ManagementObject> consume)
        {
            try
            {
                using var search = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\cimv2"), new ObjectQuery(text),
                    new EnumerationOptions { ReturnImmediately = false, Timeout = TimeSpan.FromSeconds(10) });
                using var rows = search.Get();
                foreach (ManagementObject row in rows) using (row) consume(row);
            }
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            { errors.Add(text.Split("FROM")[^1].Trim() + ": " + ex.Message); }
        }
        static string Text(ManagementObject o, string key) => o[key]?.ToString() ?? "";
        static ulong Number(ManagementObject o, string key) => ulong.TryParse(Text(o, key), out ulong number) ? number : 0;
        Query("SELECT Name FROM Win32_Processor", row => cpu = Text(row, "Name"));
        Query("SELECT Capacity,Manufacturer,Speed,DeviceLocator FROM Win32_PhysicalMemory", row => ram.Add(new(Number(row, "Capacity"), Text(row, "Manufacturer"), (uint)Number(row, "Speed"), Text(row, "DeviceLocator"))));
        Query("SELECT DeviceID,Size,FreeSpace,FileSystem FROM Win32_LogicalDisk", row => logical.Add(new(Text(row, "DeviceID"), Number(row, "Size"), Number(row, "FreeSpace"), Text(row, "FileSystem"))));
        Query("SELECT Model,Size,InterfaceType FROM Win32_DiskDrive", row => physical.Add(new(Text(row, "Model"), Number(row, "Size"), Text(row, "InterfaceType"))));
        List<SoftwareInfo> software = [];
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var uninstall = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                foreach (string child in uninstall?.GetSubKeyNames() ?? [])
                {
                    using var entry = uninstall!.OpenSubKey(child);
                    string? name = entry?.GetValue("DisplayName") as string;
                    if (!string.IsNullOrWhiteSpace(name)) software.Add(new(name, entry?.GetValue("DisplayVersion") as string ?? ""));
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { errors.Add("Registry: " + ex.Message); }
        }
        List<PrinterStatusInfo> printers = [];
        try { printers = PrinterWatchdog.GetPrinters(); } catch { }
        return new(new(cpu, ram.Aggregate(0UL, (sum, slot) => sum + slot.CapacityBytes), ram.ToArray(), logical.ToArray(), physical.ToArray(), errors.ToArray(), printers.ToArray()),
            software.DistinctBy(x => (x.Name, x.Version)).OrderBy(x => x.Name).Take(5000).ToArray());
    }
}
