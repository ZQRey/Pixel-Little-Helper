using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LitleHelperServer;

public sealed record AiCatalogModel(string Id, string Provider, string Name, string Price);

// Discovery is advisory: only an administrator can copy entries into the execution allowlist.
public sealed class AiModelCatalog(IHttpClientFactory factory, AiWorkerLifecycle lifecycle)
{
    public async Task<IReadOnlyList<AiCatalogModel>> ReadAsync(AiOptions options, CancellationToken token)
    {
        await using var lease = await lifecycle.AcquireAsync(options, token); token = lease.Token;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        bool code = options.Backend == "OpenCode";
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(AiSettingsStore.ValidateUrl(code ? options.OpenCodeUrl : options.BaseUrl, code), code ? "provider" : "models"));
        request.Headers.Authorization = code
            ? new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("opencode:" + options.OpenCodePassword)))
            : options.ApiKey.Length > 0 ? new AuthenticationHeaderValue("Bearer", options.ApiKey) : null;
        using var http = factory.CreateClient("AiProvider");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        AiBackendHttp.Check(response);
        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(chunk, timeout.Token)) > 0)
        {
            if (buffer.Length + count > 8 * 1024 * 1024) throw new ArgumentException("Model catalog exceeds 8 MiB");
            buffer.Write(chunk, 0, count);
        }
        using var json = JsonDocument.Parse(buffer.ToArray());
        return Parse(json.RootElement, code, !code && new Uri(options.BaseUrl).Host == "openrouter.ai" ? "openrouter" : "");
    }

    public static IReadOnlyList<AiCatalogModel> Parse(JsonElement root, bool code, string cloudProvider = "")
    {
        if (root.ValueKind != JsonValueKind.Object) throw new ArgumentException("Invalid model catalog");
        var result = new List<AiCatalogModel>();
        static string Text(JsonElement item, string key) => item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        static bool TextPart(JsonElement item, string direction)
        {
            if (!item.TryGetProperty("capabilities", out var capabilities) || capabilities.ValueKind != JsonValueKind.Object) return true;
            if (!capabilities.TryGetProperty(direction, out var parts) || parts.ValueKind != JsonValueKind.Object) return true;
            return !parts.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.False;
        }
        static bool TextInput(JsonElement item) => TextPart(item, "input");
        static bool TextOutput(JsonElement item) => TextPart(item, "output");
        void Add(JsonElement item, string fallback, string provider)
        {
            string id = Text(item, "id"); if (id.Length == 0) id = fallback;
            if (id.Length is 0 or > 200 || provider.Length > 100 || id.Any(char.IsControl) || provider.Any(char.IsControl)) return;
            string name = Text(item, "name"); if (name.Length > 256) name = name[..256];
            string price = "Unknown";
            if (item.TryGetProperty(code ? "cost" : "pricing", out var costs) && costs.ValueKind == JsonValueKind.Object)
            {
                bool valid = true, paid = false; int required = 0;
                foreach (var field in costs.EnumerateObject())
                {
                    if (field.Name == (code ? "input" : "prompt") || field.Name == (code ? "output" : "completion")) required++;
                    // OpenCode nests cache/tier information as objects; only scalar price components affect the free label.
                    if (field.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null) continue;
                    string raw = field.Value.ValueKind == JsonValueKind.String ? field.Value.GetString()! : field.Value.GetRawText();
                    if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal amount) || amount < 0) valid = false;
                    else if (amount > 0) paid = true;
                    else if (raw.Split('e', 'E')[0].Any(c => c is >= '1' and <= '9')) valid = false; // Decimal underflow must not turn a tiny charge into free.
                }
                price = paid ? "Paid" : valid && required == 2 ? "Free" : "Unknown";
            }
            // OpenRouter's paid IDs cannot become executable through a misleading zero-price entry.
            if (price == "Free" && provider == "openrouter" && id != "openrouter/free" && !id.EndsWith(":free", StringComparison.Ordinal)) price = "Unknown";
            result.Add(new(id, provider, name.Length > 0 ? name : id, price));
            if (result.Count > 10000) throw new ArgumentException("Too many catalog models");
        }
        if (code)
        {
            if (!root.TryGetProperty("all", out var providers) || providers.ValueKind != JsonValueKind.Array) throw new ArgumentException("Invalid OpenCode catalog");
            // OpenCode returns its full bundled provider database. Only authenticated providers can actually run;
            // the built-in "opencode" provider is listed as connected even without a key because its free models are usable anonymously.
            HashSet<string>? connected = root.TryGetProperty("connected", out var connectedList) && connectedList.ValueKind == JsonValueKind.Array
                ? connectedList.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()!).ToHashSet(StringComparer.Ordinal)
                : null;
            foreach (var provider in providers.EnumerateArray())
            {
                string providerId = provider.ValueKind == JsonValueKind.Object ? Text(provider, "id") : "";
                if (providerId.Length == 0 || (connected is not null && !connected.Contains(providerId))) continue;
                if (!provider.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Object) continue;
                foreach (var model in models.EnumerateObject())
                    if (model.Value.ValueKind == JsonValueKind.Object && TextInput(model.Value) && TextOutput(model.Value)) Add(model.Value, model.Name, providerId);
            }
        }
        else
        {
            if (!root.TryGetProperty("data", out var models) || models.ValueKind != JsonValueKind.Array) throw new ArgumentException("Invalid cloud catalog");
            foreach (var model in models.EnumerateArray()) if (model.ValueKind == JsonValueKind.Object) Add(model, "", cloudProvider);
        }
        return result.DistinctBy(m => (m.Provider, m.Id)).OrderBy(m => m.Price == "Free" ? 0 : m.Price == "Unknown" ? 2 : 1)
            .ThenBy(m => m.Provider, StringComparer.Ordinal).ThenBy(m => m.Id, StringComparer.Ordinal).ToArray();
    }
}
