using System.Diagnostics;
using System.ComponentModel;

namespace PixelHelper;

public sealed record ProcessEntry(int Pid, string Name, long? MemoryBytes, string? StartTimeUtcTicks, bool CanStop);
public sealed record ProcessTarget(int Pid, string Name, string StartTimeUtcTicks);

public static class ProcessInspector
{
    public static ProcessEntry[] Collect()
    {
        List<ProcessEntry> entries = [];
        foreach (var process in Process.GetProcesses())
        using (process)
        {
            try
            {
                int pid = process.Id;
                string name = process.ProcessName;
                long? memory = null; string? started = null;
                try { memory = process.WorkingSet64; } catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
                try { started = process.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture); } catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
                entries.Add(new(pid, name, memory, started, pid > 4 && pid != Environment.ProcessId && started != null));
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { /* Process exited during enumeration. */ }
        }
        return entries.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Pid).ToArray();
    }

    public static (string Output, int ExitCode) Stop(ProcessTarget target)
    {
        if (target.Pid <= 4 || target.Pid == Environment.ProcessId) return ("Нельзя завершить системный процесс или самого помощника", 5);
        using var process = Process.GetProcessById(target.Pid);
        if (!string.Equals(process.ProcessName, target.Name, StringComparison.OrdinalIgnoreCase) ||
            process.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) != target.StartTimeUtcTicks)
            return ("Процесс изменился. Обновите список и выберите его заново.", 87);
        process.Kill();
        return ($"Завершён процесс {target.Name}, PID {target.Pid}", 0);
    }
}
