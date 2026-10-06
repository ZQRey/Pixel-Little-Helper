using Microsoft.Win32;
using System.Diagnostics;
using System.Text;
using System.Windows.Threading;

namespace PixelHelper;

internal static class UpdateRestart
{
    internal static void Watch(System.Windows.Window window)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += (_, _) =>
        {
            try
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = machine.OpenSubKey(@"Software\PixelHelper");
                if (key?.GetValue("UpdateUntil") is not long until || DateTimeOffset.UtcNow.ToUnixTimeSeconds() > until ||
                    !Version.TryParse(key.GetValue("UpdateRequested") as string, out var target)) return;
                var current = typeof(UpdateRestart).Assembly.GetName().Version!;
                if (target <= new Version(current.Major, current.Minor, current.Build)) return;
                string exe = Environment.ProcessPath!.Replace("'", "''");
                // The watcher runs under the same desktop user and does not hold app files open.
                string script = $"$exe='{exe}';$target=[version]'{target}';$end=(Get-Date).AddMinutes(6);while((Get-Date)-lt $end){{Start-Sleep -Seconds 5;try{{$version=[version]([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion);$service=Get-Service PixelHelperUpdater -ErrorAction Stop;if($version-ge $target -and $service.Status-eq 'Running'){{Start-Sleep -Seconds 5;Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden;exit}}}}catch{{}}}};if(Test-Path -LiteralPath $exe){{Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden}}";
                var start = new ProcessStartInfo(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(arg);
                Process.Start(start)?.Dispose(); timer.Stop(); window.Close();
            }
            catch (Exception ex) { Settings.Log(ex); }
        };
        timer.Start(); window.Closed += (_, _) => timer.Stop();
    }
}
