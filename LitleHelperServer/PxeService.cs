using System.Net.Http.Json;
using System.Text.Json;

namespace LitleHelperServer;

public class PxeService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IntegrationSettings _settings;
    private readonly ILogger<PxeService> _logger;

    public PxeService(IHttpClientFactory httpClientFactory, IntegrationSettings settings, ILogger<PxeService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    private HttpClient CreateClient(string? customUrl = null)
    {
        var options = _settings.Pxe();
        var url = !string.IsNullOrWhiteSpace(customUrl) ? customUrl.Trim().TrimEnd('/') : options.ServerUrl.Trim().TrimEnd('/');
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(url);
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 10);
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            client.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
        }
        return client;
    }

    public async Task<object> TestConnectionAsync(string? targetUrl = null, CancellationToken token = default)
    {
        try
        {
            var client = CreateClient(targetUrl);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var response = await client.GetAsync("/api/status", token);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
                return new { success = true, latencyMs = sw.ElapsedMilliseconds, data = content, message = "Подключение к PXE-серверу успешно." };
            }
            return new { success = false, statusCode = (int)response.StatusCode, message = $"PXE-сервер вернул статус: {response.StatusCode}" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to PXE server at {Url}", targetUrl ?? _settings.Pxe().ServerUrl);
            return new { success = false, message = $"Не удалось подключиться к PXE-серверу: {ex.Message}" };
        }
    }

    public async Task<JsonElement?> GetStatusAsync(CancellationToken token = default)
    {
        var client = CreateClient();
        return await client.GetFromJsonAsync<JsonElement>("/api/status", token);
    }

    public async Task<JsonElement?> ServiceActionAsync(string service, string action, CancellationToken token = default)
    {
        var client = CreateClient();
        using var response = await client.PostAsync($"/api/service/{Uri.EscapeDataString(service)}/{Uri.EscapeDataString(action)}", null, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
    }

    public async Task<JsonElement?> GetImagesAsync(CancellationToken token = default)
    {
        var client = CreateClient();
        return await client.GetFromJsonAsync<JsonElement>("/api/images", token);
    }

    public async Task<bool> DeleteImageAsync(string imagePath, CancellationToken token = default)
    {
        var client = CreateClient();
        using var response = await client.DeleteAsync($"/api/images?path={Uri.EscapeDataString(imagePath)}", token);
        return response.IsSuccessStatusCode;
    }

    public async Task<JsonElement?> UploadImageAsync(HttpRequest request, CancellationToken token = default)
    {
        var client = CreateClient();
        client.Timeout = TimeSpan.FromHours(2);
        
        using var streamContent = new StreamContent(request.Body);
        if (request.ContentType != null)
        {
            streamContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(request.ContentType);
        }

        using var response = await client.PostAsync("/api/upload", streamContent, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
    }

    public async Task<JsonElement?> GetLogsAsync(string service, CancellationToken token = default)
    {
        var client = CreateClient();
        return await client.GetFromJsonAsync<JsonElement>($"/api/logs/{Uri.EscapeDataString(service)}", token);
    }

    public async Task<JsonElement?> GetConfigAsync(string type, CancellationToken token = default)
    {
        var client = CreateClient();
        return await client.GetFromJsonAsync<JsonElement>($"/api/configs/{Uri.EscapeDataString(type)}", token);
    }

    public async Task<JsonElement?> SaveConfigAsync(string type, string content, CancellationToken token = default)
    {
        var client = CreateClient();
        using var response = await client.PostAsJsonAsync($"/api/configs/{Uri.EscapeDataString(type)}", new { content }, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
    }

    public async Task<JsonElement?> ProcessWindowsAsync(object payload, CancellationToken token = default)
    {
        var client = CreateClient();
        using var response = await client.PostAsJsonAsync("/api/process-windows", payload, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
    }
}
