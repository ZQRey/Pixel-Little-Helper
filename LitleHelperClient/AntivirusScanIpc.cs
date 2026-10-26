using System.IO;
using System.IO.Pipes;
using System.Text;

namespace PixelHelper;

internal static class AntivirusScanIpc
{
    private static readonly string PipeName = "PixelHelperScanPipe-" + Environment.UserName;
    private static CancellationTokenSource? cts;

    internal static bool SendScanPathToRunningInstance(string targetPath)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(targetPath);
            return true;
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
            return false;
        }
    }

    internal static void StartServer(Action<string> onPathReceived)
    {
        cts?.Cancel();
        cts = new CancellationTokenSource();
        var token = cts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string? line = await reader.ReadLineAsync(token);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        onPathReceived(line.Trim());
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Settings.Log(ex);
                    await Task.Delay(500, token);
                }
            }
        }, token);
    }

    internal static void StopServer()
    {
        cts?.Cancel();
    }
}
