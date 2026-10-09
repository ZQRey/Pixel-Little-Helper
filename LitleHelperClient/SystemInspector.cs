using Microsoft.Win32;
using System.Management;
using System.Net;
using System.Net.Sockets;

namespace PixelHelper;

public static class SystemInspector
{
    [System.Runtime.InteropServices.DllImport("secur32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern int GetUserNameEx(int nameFormat, System.Text.StringBuilder userName, ref uint userNameSize);

    public static string GetUserFullName()
    {
        try
        {
            var sb = new System.Text.StringBuilder(260);
            uint size = (uint)sb.Capacity;
            if (GetUserNameEx(3, sb, ref size) != 0 && sb.Length > 0)
            {
                string full = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(full)) return full;
            }
        }
        catch { }

        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT FullName FROM Win32_UserAccount WHERE Name = '{Environment.UserName}'");
            foreach (ManagementObject user in searcher.Get())
            {
                string? fn = user["FullName"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(fn)) return fn;
            }
        }
        catch { }

        return Environment.UserName;
    }

    public static MachineInfo Machine()
    {
        string ip = "";
        try { ip = Dns.GetHostAddresses(Environment.MachineName).FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x))?.ToString() ?? ""; }
        catch (SocketException) { }
        return new(Environment.MachineName, Environment.UserName, Environment.UserDomainName, ip, Environment.OSVersion.VersionString, GetUserFullName());
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
        return new(new(cpu, ram.Aggregate(0UL, (sum, slot) => sum + slot.CapacityBytes), ram.ToArray(), logical.ToArray(), physical.ToArray(), errors.ToArray()),
            software.DistinctBy(x => (x.Name, x.Version)).OrderBy(x => x.Name).Take(5000).ToArray());
    }
}
