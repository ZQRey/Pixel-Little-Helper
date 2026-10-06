using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace PixelHelper;

public sealed record AssistantAction(string Id, string Title, string Type, string? Target = null, string[]? Arguments = null);

public sealed class ApiClient : IDisposable
{
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 1_048_576 };
    private readonly Settings settings;
    private readonly string cache;
    public ApiClient(Settings settings, string? cacheDirectory = null)
    {
        this.settings = settings;
        cache = Path.Combine(cacheDirectory ?? Settings.Folder, "actions_cache.json");
    }
    public static List<AssistantAction> Defaults() =>
    [
        new("search", "Поиск файлов и папок", "open_url", "search-ms:"),
        new("dmed", "Открыть DMED", "open_url", "https://krg.dmed.kz"),
        new("eisz", "Открыть EISZ", "open_url", "https://www.eisz.kz"),
        new("disable-startup", "Отключить автозапуск", "disable_startup"),
        new("exit", "Закрыть помощника", "exit")
    ];
    private Uri Endpoint(string path)
    {
        if (!Uri.TryCreate(settings.ServerUrl?.TrimEnd('/') + "/", UriKind.Absolute, out var root) || root.Scheme is not ("https" or "http"))
            throw new InvalidOperationException("Укажите serverUrl в config.json.");
        return new Uri(root, path);
    }
    private static List<AssistantAction> Validate(List<AssistantAction> values) => values
        .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Id) && !string.IsNullOrWhiteSpace(a.Title) && a.Title.Length <= 100 &&
            a.Type is "open_path" or "run_command" or "open_url" or "it_ticket")
        .DistinctBy(a => a.Id).Take(12).ToList();
    public List<AssistantAction> ReadCache()
    {
        try
        {
            if (File.Exists(cache) && new FileInfo(cache).Length <= 1_048_576)
            {
                var values = Validate(JsonSerializer.Deserialize<List<AssistantAction>>(File.ReadAllText(cache), Settings.Json) ?? []);
                if (values.Count > 0) return values;
            }
        }
        catch (Exception ex) { Settings.Log(ex); }
        return Defaults();
    }
    public async Task<List<AssistantAction>> GetActionsAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(settings.ServerUrl)) return ReadCache();
        try
        {
            var uri = Endpoint($"api/v1/assistant/actions?user={Uri.EscapeDataString(Environment.UserName)}&pc={Uri.EscapeDataString(Environment.MachineName)}");
            var json = await client.GetStringAsync(uri, token);
            var values = Validate(JsonSerializer.Deserialize<List<AssistantAction>>(json, Settings.Json) ?? []);
            if (values.Count == 0) throw new InvalidDataException("Сервер вернул пустой список действий.");
            Settings.AtomicWrite(cache, JsonSerializer.Serialize(values, Settings.Json));
            return values;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { Settings.Log(ex); return ReadCache(); }
    }
    public async Task SendTicketAsync(string message, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 2000) throw new ArgumentException("Введите сообщение (до 2000 символов).");
        using var response = await client.PostAsJsonAsync(Endpoint("api/v1/assistant/tickets"), new
        {
            pc = Environment.MachineName, user = Environment.UserName, text = message.Trim()
        }, token);
        response.EnsureSuccessStatusCode();
    }
    public void Dispose() => client.Dispose();
}
