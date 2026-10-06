using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace LitleHelperServer;

public class GlpiService(HttpClient http, IConfiguration config, ILogger<GlpiService> logger)
{
    private string Base => config["Glpi:BaseUrl"]!.TrimEnd('/') + "/";
    private HttpRequestMessage Request(HttpMethod method, string path, string? session = null)
    {
        var request = new HttpRequestMessage(method, Base + path);
        request.Headers.Add("App-Token", config["Glpi:AppToken"]);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (session != null) request.Headers.Add("Session-Token", session);
        else request.Headers.TryAddWithoutValidation("Authorization", "user_token " + config["Glpi:UserToken"]);
        return request;
    }
    private async Task<string> Init(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(config["Glpi:UserToken"]) || string.IsNullOrWhiteSpace(config["Glpi:AppToken"]))
            throw new InvalidOperationException("GLPI ещё не настроен: заполните AppToken и UserToken на сервере.");
        using var request = Request(HttpMethod.Get, "initSession");
        using var response = await http.SendAsync(request, token); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return json.RootElement.GetProperty("session_token").GetString() ?? throw new InvalidDataException("GLPI: нет Session-Token");
    }
    private async Task Kill(string session)
    {
        try
        {
            using var request = Request(HttpMethod.Get, "killSession", session);
            using var response = await http.SendAsync(request, CancellationToken.None);
            if (!response.IsSuccessStatusCode) logger.LogWarning("GLPI session cleanup returned {Status}", response.StatusCode);
        }
        catch (Exception ex) { logger.LogWarning("GLPI session cleanup failed: {Type}", ex.GetType().Name); }
    }
    public async Task<int> CreateTicketAsync(string username, string machineName, string text, CancellationToken token = default)
    {
        string session = await Init(token);
        try
        {
            string query = "search/User?criteria[0][field]=1&criteria[0][searchtype]=equals&criteria[0][value]=" + Uri.EscapeDataString(username) + "&forcedisplay[0]=2";
            using var search = Request(HttpMethod.Get, query, session);
            using var response = await http.SendAsync(search, token); response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            int requester = config.GetValue<int>("Glpi:ServiceUserId");
            if (json.RootElement.TryGetProperty("data", out var rows) && rows.ValueKind == JsonValueKind.Array && rows.GetArrayLength() > 0)
            {
                var first = rows[0];
                if (first.TryGetProperty("2", out var id) || first.TryGetProperty("id", out id))
                    requester = id.ValueKind == JsonValueKind.Number ? id.GetInt32() : int.Parse(id.GetString()!);
            }
            if (requester <= 0) throw new InvalidOperationException("Пользователь GLPI не найден. Укажите положительный ServiceUserId для fallback.");
            using var create = Request(HttpMethod.Post, "Ticket", session);
            create.Content = JsonContent.Create(new { input = new
            {
                name = $"[{machineName}] Заявка от {username}",
                content = text + $"\n\nКомпьютер: {machineName}\nПользователь: {username}",
                _users_id_requester = requester
            } });
            using var result = await http.SendAsync(create, token); result.EnsureSuccessStatusCode();
            using var ticket = JsonDocument.Parse(await result.Content.ReadAsStringAsync(token));
            return ticket.RootElement.GetProperty("id").GetInt32();
        }
        finally { await Kill(session); }
    }
    public async Task<JsonElement> GetTicketAsync(int id, CancellationToken token)
    {
        string session = await Init(token);
        try
        {
            using var request = Request(HttpMethod.Get, "Ticket/" + id, session);
            using var response = await http.SendAsync(request, token); response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token)); return json.RootElement.Clone();
        }
        finally { await Kill(session); }
    }
    public async Task UpdateTicketAsync(int id, int status, CancellationToken token)
    {
        if (status is < 1 or > 6) throw new ArgumentException("Неверный статус GLPI");
        string session = await Init(token);
        try
        {
            using var request = Request(HttpMethod.Put, "Ticket/" + id, session);
            request.Content = JsonContent.Create(new { input = new { id, status } });
            using var response = await http.SendAsync(request, token); response.EnsureSuccessStatusCode();
        }
        finally { await Kill(session); }
    }
}
