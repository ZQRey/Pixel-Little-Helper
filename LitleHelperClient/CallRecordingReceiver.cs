using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace PixelHelper;

/// <summary>
/// Фоновый легковесный локальный HTTP-приёмник для сохранения записанных видеозвонков
/// напрямую в стандартную системную папку «Видео» (Environment.SpecialFolder.MyVideos).
/// </summary>
public static class CallRecordingReceiver
{
    private static HttpListener? listener;
    private static CancellationTokenSource? cts;
    private static readonly object syncLock = new();
    public const int LocalPort = 49215;

    public static void Start()
    {
        lock (syncLock)
        {
            if (listener != null) return;
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{LocalPort}/");
                listener.Start();

                cts = new CancellationTokenSource();
                _ = ListenLoopAsync(listener, cts.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CallRecordingReceiver] Ошибка запуска: {ex.Message}");
            }
        }
    }

    public static void Stop()
    {
        lock (syncLock)
        {
            try
            {
                cts?.Cancel();
                listener?.Stop();
                listener?.Close();
            }
            catch { }
            finally
            {
                listener = null;
                cts = null;
            }
        }
    }

    private static async Task ListenLoopAsync(HttpListener httpListener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && httpListener.IsListening)
        {
            try
            {
                var context = await httpListener.GetContextAsync();
                _ = ProcessRequestAsync(context);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CallRecordingReceiver] Ошибка контекста: {ex.Message}");
            }
        }
    }

    private static async Task ProcessRequestAsync(HttpListenerContext context)
    {
        var req = context.Request;
        var res = context.Response;

        // Разрешаем CORS с любого локального источника
        res.Headers.Add("Access-Control-Allow-Origin", "*");
        res.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
        res.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-Filename, X-Caller");

        if (req.HttpMethod == "OPTIONS")
        {
            res.StatusCode = (int)HttpStatusCode.NoContent;
            res.Close();
            return;
        }

        if (req.Url?.AbsolutePath == "/api/save-call-recording" && req.HttpMethod == "POST")
        {
            try
            {
                string rawFilename = req.QueryString["name"] ?? req.Headers["X-Filename"] ?? "";
                if (string.IsNullOrWhiteSpace(rawFilename))
                {
                    rawFilename = $"Видеозвонок_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.webm";
                }

                // Очищаем имя от недопустимых символов
                foreach (char c in Path.GetInvalidFileNameChars())
                {
                    rawFilename = rawFilename.Replace(c, '_');
                }

                string videosFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                if (string.IsNullOrWhiteSpace(videosFolder) || !Directory.Exists(videosFolder))
                {
                    videosFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
                    Directory.CreateDirectory(videosFolder);
                }

                string targetFilePath = Path.Combine(videosFolder, rawFilename);

                // Если файл уже существует, добавляем суффикс
                if (File.Exists(targetFilePath))
                {
                    string nameNoExt = Path.GetFileNameWithoutExtension(rawFilename);
                    string ext = Path.GetExtension(rawFilename);
                    targetFilePath = Path.Combine(videosFolder, $"{nameNoExt}_{Guid.NewGuid():N}{ext}");
                }

                await using (var fs = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await req.InputStream.CopyToAsync(fs);
                }

                byte[] okBytes = Encoding.UTF8.GetBytes("{\"ok\":true,\"path\":\"" + targetFilePath.Replace("\\", "\\\\") + "\"}");
                res.ContentType = "application/json; charset=utf-8";
                res.StatusCode = (int)HttpStatusCode.OK;
                res.ContentLength64 = okBytes.Length;
                await res.OutputStream.WriteAsync(okBytes);
                res.Close();

                // Показываем уведомление пользователю
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    try
                    {
                        var answer = System.Windows.MessageBox.Show(
                            $"Запись видеозвонка успешно сохранена в папку «Видео»:\n{Path.GetFileName(targetFilePath)}\n\nОткрыть файл сейчас?",
                            "Запись видеозвонка сохранена",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Information);

                        if (answer == System.Windows.MessageBoxResult.Yes)
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = targetFilePath,
                                UseShellExecute = true
                            });
                        }
                    }
                    catch { }
                });

                return;
            }
            catch (Exception ex)
            {
                byte[] errBytes = Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"" + ex.Message.Replace("\"", "\\\"") + "\"}");
                res.ContentType = "application/json; charset=utf-8";
                res.StatusCode = (int)HttpStatusCode.InternalServerError;
                res.ContentLength64 = errBytes.Length;
                await res.OutputStream.WriteAsync(errBytes);
                res.Close();
                return;
            }
        }

        res.StatusCode = (int)HttpStatusCode.NotFound;
        res.Close();
    }
}
