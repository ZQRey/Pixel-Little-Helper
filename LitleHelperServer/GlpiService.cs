using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace LitleHelperServer;

public partial class GlpiService(HttpClient http, GlpiSettingsStore store, ILogger<GlpiService> logger)
{
    private readonly GlpiOptions settings = store.Read();
    private string Base => settings.BaseUrl.TrimEnd('/') + "/";
    private HttpRequestMessage Request(HttpMethod method, string path, string? session = null)
    {
        var request = new HttpRequestMessage(method, Base + path);
        if (!string.IsNullOrWhiteSpace(settings.AppToken)) request.Headers.Add("App-Token", settings.AppToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (session != null) request.Headers.Add("Session-Token", session);
        else if (settings.AuthMode == "password" && !string.IsNullOrEmpty(settings.Password))
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(settings.Login + ":" + settings.Password)));
        else if (settings.AuthMode == "token" && !string.IsNullOrWhiteSpace(settings.UserToken)) request.Headers.TryAddWithoutValidation("Authorization", "user_token " + settings.UserToken);
        return request;
    }
    private async Task<string> Init(CancellationToken token, bool probe = false)
    {
        if (!probe && (settings.AuthMode == "password" ? string.IsNullOrWhiteSpace(settings.Login) || string.IsNullOrEmpty(settings.Password) : string.IsNullOrWhiteSpace(settings.UserToken)))
            throw new InvalidOperationException("GLPI ещё не настроен: заполните User Token или логин/пароль во вкладке «Настройки GLPI» веб-панели.");
        using var request = Request(HttpMethod.Get, "initSession");
        using var response = await http.SendAsync(request, token); await EnsureSuccess(response, token);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("session_token", out var session) || session.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(session.GetString()))
            throw new InvalidOperationException("GLPI не вернул Session-Token. Проверьте адрес REST API.");
        return session.GetString()!;
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
    public async Task TestConnectionAsync(CancellationToken token)
    {
        string session = await Init(token, probe: true);
        await Kill(session);
    }
    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken token)
    {
        string body = await response.Content.ReadAsStringAsync(token);
        string code = "", errorText = body;
        bool jsonValid = false;
        try
        {
            using var json = JsonDocument.Parse(body);
            jsonValid = true;
            if (json.RootElement.ValueKind == JsonValueKind.Array && json.RootElement.GetArrayLength() > 0 && json.RootElement[0].ValueKind == JsonValueKind.String)
                {
                code = json.RootElement[0].GetString() ?? "";
                if (json.RootElement.GetArrayLength() > 1 && json.RootElement[1].ValueKind == JsonValueKind.String) errorText = json.RootElement[1].GetString() ?? body;
            }
        }
        catch (JsonException) { }
        if (errorText.Contains("API отключ", StringComparison.OrdinalIgnoreCase) || errorText.Contains("API is disabled", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("GLPI REST API отключён. В GLPI откройте Настройка → Общие → API и включите REST API.");
        string? detail = code switch
        {
            "ERROR_NOT_ALLOWED_IP" => "GLPI не разрешает IP этого сервера. Добавьте IP сервера помощника в разрешённый диапазон API-клиента GLPI.",
            "ERROR_APP_TOKEN_PARAMETERS_MISSING" => "GLPI требует App Token. Заполните его во вкладке «Настройки GLPI».",
            "ERROR_WRONG_APP_TOKEN_PARAMETER" => "GLPI отклонил App Token. Проверьте ключ API-клиента.",
            "ERROR_LOGIN_PARAMETERS_MISSING" => "GLPI требует User Token или логин/пароль. Заполните настройки доступа.",
            "ERROR_GLPI_LOGIN_USER_TOKEN" or "ERROR_GLPI_LOGIN" => "GLPI отклонил учётные данные. Проверьте User Token или логин/пароль.",
            "ERROR_LOGIN_WITH_CREDENTIALS_DISABLED" => "В GLPI отключён API-вход по логину/паролю. Используйте User Token или включите такой способ входа.",
            "ERROR_RIGHT_MISSING" => "Учётная запись GLPI не имеет прав на это действие. Проверьте профиль, сущность и права на заявки/пользователей.",
            _ => null
        };
        if (detail != null) throw new InvalidOperationException(detail);
        if (!response.IsSuccessStatusCode || code.StartsWith("ERROR", StringComparison.Ordinal))
            throw new InvalidOperationException($"GLPI API вернул HTTP {(int)response.StatusCode}. Проверьте настройки API и права учётной записи.");
        if (response.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException("Вместо REST API GLPI вернул страницу входа. Укажите адрес, заканчивающийся /apirest.php. Автовход браузера не авторизует серверное API.");
        if (!jsonValid) throw new InvalidOperationException("GLPI вернул некорректный JSON. Проверьте адрес REST API и настройки прокси.");
    }
    public async Task<int?> UploadDocumentAsync(int ticketId, string fileName, byte[] content, string session, CancellationToken token = default)
    {
        try
        {
            using var request = Request(HttpMethod.Post, "Document", session);
            var multipart = new MultipartFormDataContent();
            var manifest = JsonSerializer.Serialize(new { input = new { name = fileName, _filename = new[] { fileName } } });
            multipart.Add(new StringContent(manifest), "uploadManifest");
            var fileContent = new ByteArrayContent(content);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipart.Add(fileContent, "filename[0]", fileName);
            request.Content = multipart;
            using var result = await http.SendAsync(request, token);
            if (!result.IsSuccessStatusCode)
            {
                logger.LogWarning("GLPI UploadDocument failed with status {Status}", result.StatusCode);
                return null;
            }
            using var json = JsonDocument.Parse(await result.Content.ReadAsStringAsync(token));
            if (!json.RootElement.TryGetProperty("id", out var idProp)) return null;
            int docId = idProp.GetInt32();

            using var linkRequest = Request(HttpMethod.Post, "Document_Item", session);
            linkRequest.Content = JsonContent.Create(new { input = new { documents_id = docId, items_id = ticketId, itemtype = "Ticket" } });
            using var linkResult = await http.SendAsync(linkRequest, token);
            if (!linkResult.IsSuccessStatusCode)
            {
                logger.LogWarning("GLPI Document_Item link failed with status {Status}", linkResult.StatusCode);
            }
            return docId;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to upload document '{FileName}' to GLPI ticket #{TicketId}", fileName, ticketId);
            return null;
        }
    }
    public async Task<int> CreateTicketAsync(string username, string machineName, string text, CancellationToken token = default)
        => await CreateTicketAsync(username, machineName, text, (IEnumerable<TicketAttachmentDto>?)null, token);

    public async Task<int> CreateTicketAsync(string username, string machineName, string text, IEnumerable<TicketAttachmentDto>? attachments, CancellationToken token = default)
    {
        string session = await Init(token);
        try
        {
            logger.LogInformation("GLPI CreateTicketAsync: username='{Username}', machine='{MachineName}'", username, machineName);
            int requester;
            try { requester = await FindUserInSessionAsync(username, session, token); }
            catch (GlpiUserNotFoundException) {
                requester = settings.ServiceUserId;
                logger.LogWarning("GLPI requester {Username} was not found; configured fallback is {Requester}",username,requester);
            }
            if (requester <= 0) throw new InvalidOperationException($"Пользователь GLPI '{username}' не найден. Укажите положительный ServiceUserId для fallback.");
            using var create = Request(HttpMethod.Post, "Ticket", session);
            create.Content = JsonContent.Create(new { input = new
            {
                name = $"[{machineName}] Заявка от {username}",
                content = text + $"\n\nКомпьютер: {machineName}\nПользователь: {username}",
                _users_id_requester = requester
            } });
            using var result = await http.SendAsync(create, token); await EnsureSuccess(result, token);
            using var ticket = JsonDocument.Parse(await result.Content.ReadAsStringAsync(token));
            int ticketId = ticket.RootElement.GetProperty("id").GetInt32();
            logger.LogInformation("GLPI заявка #{TicketId} успешно создана для пользователя '{Username}' (Requester ID={Requester})", ticketId, username, requester);

            if (attachments != null)
            {
                foreach (var att in attachments)
                {
                    await UploadDocumentAsync(ticketId, att.FileName, att.Content, session, token);
                }
            }

            return ticketId;
        }
        finally { await Kill(session); }
    }
    public async Task<JsonElement> GetTicketAsync(int id, CancellationToken token)
    {
        string session = await Init(token);
        try
        {
            using var request = Request(HttpMethod.Get, "Ticket/" + id, session);
            using var response = await http.SendAsync(request, token); await EnsureSuccess(response, token);
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
            using var response = await http.SendAsync(request, token); await EnsureSuccess(response, token);
        }
        finally { await Kill(session); }
    }

    private async Task<JsonElement> CallAsync(HttpMethod method, string path, object? input, CancellationToken token)
    {
        string session = await Init(token);
        try
        {
            using var request = Request(method, path, session);
            if (input != null) request.Content = JsonContent.Create(new { input });
            using var response = await http.SendAsync(request, token); await EnsureSuccess(response, token);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token)); return json.RootElement.Clone();
        }
        finally { await Kill(session); }
    }
    public static int Number(JsonElement value) => value.ValueKind == JsonValueKind.Number ? value.GetInt32() : int.Parse(value.GetString()!);
    public async Task<int> FindTechnicianAsync(string account,CancellationToken token) {
        string session=await Init(token);
        try {return await FindUserInSessionAsync(account,session,token);}
        finally {await Kill(session);}
    }
    private async Task<JsonElement> ReadInSessionAsync(string path,string session,CancellationToken token) {
        using var request=Request(HttpMethod.Get,path,session);
        using var response=await http.SendAsync(request,token);await EnsureSuccess(response,token);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));return json.RootElement.Clone();
    }
    private async Task<int> FindUserInSessionAsync(string account,string session,CancellationToken token)
    {
        // GLPI's User login is an itemlink: equals compares the numeric ID.
        // Anchored text search compares the complete login instead.
        var json = await ReadInSessionAsync("search/User?criteria[0][field]=1&criteria[0][searchtype]=contains&criteria[0][value]=" + Uri.EscapeDataString("^" + account + "$") + "&forcedisplay[0]=2&range=0-2", session, token);
        if (!json.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() == 0)
            throw new GlpiUserNotFoundException("Пользователь GLPI не найден.");
        if (rows.GetArrayLength() != 1 || (json.TryGetProperty("totalcount",out var total) && Number(total) != 1))
            throw new InvalidOperationException("В GLPI должен существовать ровно один пользователь с логином AD " + account + ".");
        int id = Number(rows[0].GetProperty("2"));
        var user = await ReadInSessionAsync("User/" + id, session, token);
        if (!user.GetProperty("name").GetString()!.Equals(account, StringComparison.OrdinalIgnoreCase) || Number(user.GetProperty("is_active")) != 1 || Number(user.GetProperty("is_deleted")) != 0)
            throw new InvalidOperationException("Пользователь GLPI отключён или его логин не совпадает с AD.");
        return id;
    }
    public async Task<int[]> AssigneesAsync(int id, CancellationToken token)
    {
        var rows = await CallAsync(HttpMethod.Get, "Ticket/" + id + "/Ticket_User?range=0-999", null, token);
        return rows.EnumerateArray().Where(r => Number(r.GetProperty("type")) == 2).Select(r => Number(r.GetProperty("users_id"))).Distinct().ToArray();
    }
    public async Task AssignAsync(int id, int userId, CancellationToken token) =>
        await CallAsync(HttpMethod.Put, "Ticket/" + id, new { id, _users_id_assign = userId, status = 2 }, token);
    public async Task FollowupAsync(int id, string username, string text, CancellationToken token)
    {
        var result = await CallAsync(HttpMethod.Post, "ITILFollowup", new { itemtype = "Ticket", items_id = id, content = System.Net.WebUtility.HtmlEncode("[Telegram · " + username + "]\n" + text).Replace("\n", "<br>"), is_private = 0 }, token);
        if (!result.TryGetProperty("id", out var created) || Number(created) <= 0) throw new InvalidOperationException("GLPI не подтвердил сохранение ответа. Проверьте заявку перед повтором.");
    }
    public async Task SolveAsync(int id, string username, string text, CancellationToken token)
    {
        var result = await CallAsync(HttpMethod.Post, "ITILSolution", new { itemtype = "Ticket", items_id = id, content = System.Net.WebUtility.HtmlEncode("[Telegram · " + username + "]\n" + text).Replace("\n", "<br>") }, token);
        if (!result.TryGetProperty("id", out var created) || Number(created) <= 0) throw new InvalidOperationException("GLPI не подтвердил сохранение решения. Проверьте заявку перед повтором.");
    }
    public async Task<JsonElement> FollowupsAsync(int id, CancellationToken token) =>
        await CallAsync(HttpMethod.Get, "Ticket/" + id + "/ITILFollowup?range=0-999", null, token);
    public async Task<JsonElement> SolutionsAsync(int id, CancellationToken token)
    {
        try { return await CallAsync(HttpMethod.Get, "Ticket/" + id + "/ITILSolution?range=0-999", null, token); }
        catch { return default; }
    }
    public async Task<JsonElement> SatisfactionAsync(int id, CancellationToken token)
    {
        try { return await CallAsync(HttpMethod.Get, "Ticket/" + id + "/TicketSatisfaction?range=0-999", null, token); }
        catch { return default; }
    }
}

public sealed class GlpiUserNotFoundException(string message) : InvalidOperationException(message);
