using System.IO;
using System.IO.Pipes;
using System.Text;

namespace PixelHelper;

internal static class WebGuardIpc
{
    private static readonly string PipeName = "PixelHelperGuardPipe-" + Environment.UserName;
    private static CancellationTokenSource? cts;

    internal static bool SendUrlToRunningInstance(string url, bool launchOnFailure = false)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(url);
            return true;
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
            if (launchOnFailure) WebGuard.LaunchUrl(url);
            return false;
        }
    }

    internal static void StartServer(Action<string> onUrlReceived)
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
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string? line = await reader.ReadLineAsync(token);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        onUrlReceived(line.Trim());
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
