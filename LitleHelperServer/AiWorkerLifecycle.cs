using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace LitleHelperServer;

public sealed record AiWorkerRuntime(string Phase, int ActiveLeases = 0, int LeaseSeconds = 120, int IdleSeconds = 600);

// This client has no Docker access. The host controller accepts only a pre-generated owner pool.
public sealed class AiWorkerLifecycle(IHttpClientFactory factory, IConfiguration configuration)
{
    public bool Enabled => !string.IsNullOrWhiteSpace(configuration["Ai:WorkerControllerUrl"]);
    private async Task<AiWorkerRuntime> CallAsync(AiOptions options, string operation, CancellationToken token)
    {
        string address = configuration["Ai:WorkerControllerUrl"] ?? "";
        string tokenFile = configuration["Ai:WorkerControllerTokenFile"] ?? "";
        string secret = (await File.ReadAllTextAsync(tokenFile, token)).Trim();
        if (secret.Length < 32 || secret.Any(char.IsWhiteSpace)) throw new InvalidOperationException("Worker controller token is not configured");
        using var http = factory.CreateClient("AiWorkerController");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(AiSettingsStore.ValidateUrl(address, true), $"workers/{options.WorkerOwnerId}/{operation}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        request.Content = JsonContent.Create(new { url = options.OpenCodeUrl });
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) throw new ArgumentException($"Personal worker controller HTTP {(int)response.StatusCode}; no fallback was performed");
        using var json = await AiBackendHttp.JsonAsync(response, token);
        return json.RootElement.Deserialize<AiWorkerRuntime>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Worker controller returned no status");
    }
    public async Task<AiWorkerRuntime> StatusAsync(AiOptions options, CancellationToken token)
    {
        if (!Enabled) return new("Manual");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        return await CallAsync(options, "status", timeout.Token);
    }
    public async Task<AiWorkerRuntime> StopAsync(AiOptions options, CancellationToken token)
    {
        if (!Enabled) throw new ArgumentException("Worker lifecycle is managed manually on this server");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(35));
        return await CallAsync(options, "stop", timeout.Token);
    }
    public async Task<Lease> AcquireAsync(AiOptions options, CancellationToken token)
    {
        if (options.WorkerOwnerId <= 0 || !Enabled) return new Lease(token);
        string id = Guid.NewGuid().ToString("N");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(80));
            var status = await CallAsync(options, "leases/" + id, timeout.Token);
            if (status.Phase != "Running" || status.LeaseSeconds is < 60 or > 600) throw new InvalidOperationException("Worker controller did not grant a safe lease");
            return new Lease(this, options, id, token);
        }
        catch
        {
            await ReleaseAsync(options, id);
            throw;
        }
    }
    private async Task ReleaseAsync(AiOptions options, string id)
    {
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await CallAsync(options, "release/" + id, timeout.Token); }
        catch { /* Lease expires on the host even if release cannot be delivered. */ }
    }
    public sealed class Lease : IAsyncDisposable
    {
        private readonly AiWorkerLifecycle? controller;
        private readonly AiOptions? options;
        private readonly string id = "";
        private readonly CancellationTokenSource? run;
        private readonly CancellationTokenSource stop = new();
        private readonly Task renewal = Task.CompletedTask;
        public CancellationToken Token { get; }
        internal Lease(CancellationToken token) => Token = token;
        internal Lease(AiWorkerLifecycle controller, AiOptions options, string id, CancellationToken token)
        {
            this.controller = controller; this.options = options; this.id = id;
            run = CancellationTokenSource.CreateLinkedTokenSource(token); Token = run.Token;
            renewal = RenewAsync();
        }
        private async Task RenewAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(25), stop.Token);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    var status = await controller!.CallAsync(options!, "leases/" + id, timeout.Token);
                    if (status.Phase != "Running") throw new InvalidOperationException("Worker lease was lost");
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch { run!.Cancel(); }
        }
        public async ValueTask DisposeAsync()
        {
            stop.Cancel(); await renewal;
            if (controller is not null) await controller.ReleaseAsync(options!, id);
            run?.Dispose(); stop.Dispose();
        }
    }
}
