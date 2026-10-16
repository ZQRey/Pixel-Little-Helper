using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace PixelHelper;

public sealed record AssistantAction(string Id, string Title, string Type, string? Target = null, string[]? Arguments = null);

public sealed class ApiClient : IDisposable
{
    private readonly HttpClient client = new(new HttpClientHandler {AllowAutoRedirect=false}) { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 1_048_576 };
    private readonly Settings settings;
    public ApiClient(Settings settings) => this.settings=settings;
    public static List<AssistantAction> Defaults() =>
    [
        new("search", "Поиск файлов и папок", "open_url", "search-ms:"),
        new("dmed", "Открыть DMED", "open_url", "https://krg.dmed.kz"),
        new("eisz", "Открыть EISZ", "open_url", "https://www.eisz.kz"),
        new("exit", "Закрыть помощника", "exit")
    ];
    private Uri Endpoint(string path)
    {
        if (!Uri.TryCreate(settings.ServerUrl?.TrimEnd('/') + "/", UriKind.Absolute, out var root) || root.Scheme is not ("https" or "http"))
            throw new InvalidOperationException("Укажите serverUrl в config.json.");
        return new Uri(root, path);
    }
    public async Task<(bool Success, string Message)> ChangeAdPasswordAsync(string username, string newPassword, string currentPassword, CancellationToken token = default)
    {
        try
        {
            using var req = PasswordChangeRequest(username,newPassword,currentPassword);
            using var resp = await client.SendAsync(req, token);
            var content = await resp.Content.ReadAsStringAsync(token);
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
            bool success = doc.RootElement.TryGetProperty("success", out var sProp) && sProp.GetBoolean();
            string msg = doc.RootElement.TryGetProperty("message", out var mProp) ? mProp.GetString() ?? "" : "";
            return (resp.IsSuccessStatusCode && success, string.IsNullOrWhiteSpace(msg) ? $"Сервер отклонил запрос (HTTP {(int)resp.StatusCode})." : msg);
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
            return (false, "Не удалось связаться с сервером: " + ex.Message);
        }
    }
    internal HttpRequestMessage PasswordChangeRequest(string username,string newPassword,string currentPassword) {
        var uri=Endpoint("api/ad/change-password");
        if(uri.Scheme=="http")uri=new UriBuilder(uri) {Scheme="https",Port=uri.IsDefaultPort?443:uri.Port}.Uri;
        var request=new HttpRequestMessage(HttpMethod.Post,uri);
        request.Headers.Add("X-Client-Key",settings.ClientToken);
        request.Headers.Add("X-Machine-Name",Environment.MachineName);
        request.Content=JsonContent.Create(new {username,newPassword,currentPassword});
        return request;
    }
    public void Dispose() => client.Dispose();
}
