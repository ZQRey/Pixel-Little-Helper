using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace LitleHelperServer;

public sealed record AiTurn(string Role, string Content);
public sealed record AiUsage(int? InputTokens = null, int? OutputTokens = null, decimal? Cost = null, string CostStatus = "Unknown");
public sealed record AiBackendChunk(string Text = "", AiUsage? Usage = null);
public interface IAiBackend
{
    Task<string> CreateAsync(AiOptions options, CancellationToken token);
    IAsyncEnumerable<AiBackendChunk> ReplyAsync(AiOptions options, AiModel model, string externalId, IReadOnlyList<AiTurn> turns, CancellationToken token);
    Task CloseAsync(AiOptions options, string externalId, bool delete, CancellationToken token);
}
public static class AiBackendHttp
{
    public const int MaxResponse = 131072;
    public static async Task<JsonDocument> JsonAsync(HttpResponseMessage response, CancellationToken token)
    {
        Check(response);
        using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[4096]; int count;
        while ((count = await stream.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + count > MaxResponse) throw new InvalidOperationException("AI response exceeded the size limit");
            buffer.Write(chunk, 0, count);
        }
        return JsonDocument.Parse(buffer.ToArray());
    }
    public static void Check(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"AI provider HTTP {(int)response.StatusCode}");
    }
}

public sealed class CloudApiBackend(IHttpClientFactory factory) : IAiBackend
{
    public Task<string> CreateAsync(AiOptions options, CancellationToken token) => Task.FromResult("");
    public Task CloseAsync(AiOptions options, string externalId, bool delete, CancellationToken token) => Task.CompletedTask;
    public async IAsyncEnumerable<AiBackendChunk> ReplyAsync(AiOptions options, AiModel model, string externalId, IReadOnlyList<AiTurn> turns, [EnumeratorCancellation] CancellationToken token)
    {
        using var http = factory.CreateClient("AiProvider");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(AiSettingsStore.ValidateUrl(options.BaseUrl, false), "chat/completions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Content = JsonContent.Create(new { model = model.Id, messages = turns.Select(t => new { role = t.Role, content = t.Content }),
            max_tokens = options.MaxOutputTokens, stream = true, stream_options = new { include_usage = true } });
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        AiBackendHttp.Check(response);
        using var stream = await response.Content.ReadAsStreamAsync(token);
        using var reader = new StreamReader(stream);
        bool completed = false; int total = 0, eventBytes = 0;
        var data = new StringBuilder();
        // Read bounded lines: StreamReader.ReadLine alone can allocate an unbounded provider response.
        var line = new StringBuilder(); var chars = new char[1];
        while (await reader.ReadAsync(chars.AsMemory(), token) != 0)
        {
            char c = chars[0];
            if (c != '\n')
            {
                if (line.Length >= 65536) throw new InvalidOperationException("AI event exceeded the size limit");
                if (c != '\r') line.Append(c);
                continue;
            }
            string value = line.ToString(); line.Clear();
            if (value.StartsWith("data:", StringComparison.Ordinal))
            {
                string part = value[5..].TrimStart();
                eventBytes += part.Length;
                if (eventBytes > 65536) throw new InvalidOperationException("AI event exceeded the size limit");
                if (data.Length > 0) data.Append('\n');
                data.Append(part);
            }
            if (value.Length != 0 || data.Length == 0) continue;
            string payload = data.ToString(); data.Clear(); eventBytes = 0;
            if (payload == "[DONE]") { completed = true; break; }
            using var json = JsonDocument.Parse(payload);
            if (json.RootElement.TryGetProperty("error", out _)) throw new InvalidOperationException("AI provider stream failed");
            if (json.RootElement.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                yield return new(Usage: ReadUsage(usage, "prompt_tokens", "completion_tokens"));
            if (!json.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
            var choice = choices[0];
            if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                string text = content.GetString()!; total += text.Length;
                if (total > AiBackendHttp.MaxResponse) throw new InvalidOperationException("AI answer exceeded the size limit");
                if (text.Length > 0) yield return new(text);
            }
        }
        if (!completed || total == 0) throw new InvalidOperationException("AI stream was interrupted or returned an empty answer");
    }
    internal static AiUsage ReadUsage(JsonElement value, string inputName, string outputName)
    {
        int? input = value.TryGetProperty(inputName, out var i) && i.ValueKind == JsonValueKind.Number && i.TryGetInt32(out int iv) && iv >= 0 ? iv : null;
        int? output = value.TryGetProperty(outputName, out var o) && o.ValueKind == JsonValueKind.Number && o.TryGetInt32(out int ov) && ov >= 0 ? ov : null;
        decimal? cost = value.TryGetProperty("cost", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetDecimal(out decimal cv) && cv >= 0 ? cv : null;
        return new(input, output, cost, cost.HasValue ? "ProviderReported" : "Unknown");
    }
}

public sealed class OpenCodeBackend(IHttpClientFactory factory, AiWorkerLifecycle lifecycle) : IAiBackend
{
    public async Task RemoveCredentialAsync(AiOptions options, string provider, CancellationToken token)
    {
        await using var lease = await lifecycle.AcquireAsync(options, token); token = lease.Token;
        using var http = Client(options);
        using var response = await http.DeleteAsync("auth/" + Uri.EscapeDataString(provider), token);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound) AiBackendHttp.Check(response);
    }
    public async Task ConfigureCredentialAsync(AiOptions options, string provider, CancellationToken token)
    {
        await using var lease = await lifecycle.AcquireAsync(options, token); token = lease.Token;
        if (string.IsNullOrWhiteSpace(provider) || provider.Length > 100 || options.ApiKey.Length == 0) throw new ArgumentException("Save the provider API key and Provider ID first");
        using var http = Client(options);
        using var response = await http.PutAsJsonAsync("auth/" + Uri.EscapeDataString(provider), new { type = "api", key = options.ApiKey }, token);
        AiBackendHttp.Check(response);
    }
    private HttpClient Client(AiOptions options)
    {
        var http = factory.CreateClient("AiProvider");
        http.BaseAddress = AiSettingsStore.ValidateUrl(options.OpenCodeUrl, true);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("opencode:" + options.OpenCodePassword)));
        return http;
    }
    public async Task<string> CreateAsync(AiOptions options, CancellationToken token)
    {
        await using var lease = await lifecycle.AcquireAsync(options, token); token = lease.Token;
        using var http = Client(options);
        // Require the dedicated chat agent and deny all tools, including agent overrides.
        using var configResponse = await http.GetAsync("config", HttpCompletionOption.ResponseHeadersRead, token);
        using var config = await AiBackendHttp.JsonAsync(configResponse, token);
        var root = config.RootElement;
        if (!root.TryGetProperty("permission", out var permissions) || !DenyAll(permissions) ||
            !root.TryGetProperty("agent", out var agents) || !agents.TryGetProperty("litlehelper", out var agent) ||
            !agent.TryGetProperty("permission", out var agentPermissions) || !DenyAll(agentPermissions) ||
            (root.TryGetProperty("mcp", out var mcp) && (mcp.ValueKind != JsonValueKind.Object || mcp.EnumerateObject().Any())) ||
            (root.TryGetProperty("plugin", out var plugins) && (plugins.ValueKind != JsonValueKind.Array || plugins.GetArrayLength() > 0)))
            throw new InvalidOperationException("OpenCode requires the isolated litlehelper agent with all tools denied and no MCP servers");
        using var response = await http.PostAsJsonAsync("session", new { title = "LitleHelper", permission = new[] { new { permission = "*", pattern = "*", action = "deny" } } }, token);
        using var json = await AiBackendHttp.JsonAsync(response, token);
        string id = json.RootElement.GetProperty("id").GetString()!;
        if (id.Length is < 1 or > 200) throw new InvalidOperationException("Invalid OpenCode session");
        return id;
    }
    private static bool DenyAll(JsonElement permissions) => permissions.ValueKind == JsonValueKind.Object &&
        permissions.TryGetProperty("*", out var all) && all.ValueKind == JsonValueKind.String && all.GetString() == "deny" &&
        permissions.EnumerateObject().All(p => p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() == "deny");
    public async IAsyncEnumerable<AiBackendChunk> ReplyAsync(AiOptions options, AiModel model, string externalId, IReadOnlyList<AiTurn> turns, [EnumeratorCancellation] CancellationToken token)
    {
        await using var lease = await lifecycle.AcquireAsync(options, token); token = lease.Token;
        if (string.IsNullOrWhiteSpace(model.Provider)) throw new ArgumentException("OpenCode model requires Provider ID");
        using var http = Client(options);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"session/{Uri.EscapeDataString(externalId)}/message")
        {
            Content = JsonContent.Create(new { model = new { providerID = model.Provider, modelID = model.Id }, agent = "litlehelper",
                system = turns.First(t => t.Role == "system").Content,
                parts = new[] { new { type = "text", text = turns.Last().Content } } })
        };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        using var json = await AiBackendHttp.JsonAsync(response, token);
        if (json.RootElement.TryGetProperty("info", out var info) && info.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException("OpenCode generation failed");
        string text = string.Concat(json.RootElement.GetProperty("parts").EnumerateArray()
            .Where(p => p.TryGetProperty("type", out var type) && type.GetString() == "text" && (!p.TryGetProperty("ignored", out var ignored) || ignored.ValueKind != JsonValueKind.True))
            .Select(p => p.GetProperty("text").GetString()));
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("OpenCode returned an empty answer");
        yield return new(text);
        if (json.RootElement.TryGetProperty("info", out var details))
        {
            var usage = details.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object ? CloudApiBackend.ReadUsage(tokens, "input", "output") : new AiUsage();
            if (details.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Number && cost.TryGetDecimal(out decimal amount) && amount >= 0)
                usage = usage with { Cost = amount, CostStatus = "ProviderReported" };
            yield return new(Usage: usage);
        }
    }
    public async Task CloseAsync(AiOptions options, string externalId, bool delete, CancellationToken token)
    {
        if (externalId.Length == 0) return;
        await using var lease = await lifecycle.AcquireAsync(options, token); token = lease.Token;
        using var http = Client(options);
        string path = "session/" + Uri.EscapeDataString(externalId);
        using var response = delete ? await http.DeleteAsync(path, token) : await http.PostAsJsonAsync(path + "/abort", new { }, token);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound) AiBackendHttp.Check(response);
    }
}
