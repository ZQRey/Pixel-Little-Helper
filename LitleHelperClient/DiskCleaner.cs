using System.IO;

namespace PixelHelper;

public sealed record CleanResult(int FilesDeleted, long BytesFreed, string Message);

public static class DiskCleaner
{
    public static Task<CleanResult> CleanAsync() => CleanFoldersAsync(new[] {
        Path.GetTempPath(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data", "Default", "Cache"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "User Data", "Default", "Cache"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NCALayer", "temp"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NCALayer", "logs") });
    internal static async Task<CleanResult> CleanFoldersAsync(IEnumerable<string> folders)
    {
        return await Task.Run(() =>
        {
            int count = 0;
            long bytes = 0;

            var targetFolders = folders.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var folder in targetFolders)
            {
                if (!Directory.Exists(folder) || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;

                try
                {
                    var dirInfo = new DirectoryInfo(folder);
                    foreach (var file in dirInfo.EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories=true, IgnoreInaccessible=true, AttributesToSkip=FileAttributes.ReparsePoint }))
                    {
                        try
                        {
                            // Skip files modified in the last 60 minutes (currently in use)
                            if (DateTime.UtcNow - file.LastWriteTimeUtc < TimeSpan.FromHours(1)) continue;

                            long size = file.Length;
                            file.Delete();
                            bytes += size;
                            count++;
                        }
                        catch { }
                    }

                    // Remove empty subdirectories
                    foreach (var sub in dirInfo.GetDirectories())
                    {
                        try
                        {
                            if ((sub.Attributes & FileAttributes.ReparsePoint) == 0 && sub.GetFiles().Length == 0 && sub.GetDirectories().Length == 0)
                                sub.Delete(false);
                        }
                        catch { }
                    }
                }
                catch { }
            }

            double mb = bytes / (1024.0 * 1024.0);
            string msg = count > 0
                ? $"Очистка завершена! Удалено временных файлов: {count} ({mb:F1} МБ освобождено) 🧹"
                : "Временные файлы не требуют очистки. Система в порядке! ✨";

            return new CleanResult(count, bytes, msg);
        });
    }
}
