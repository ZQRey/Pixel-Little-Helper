using Microsoft.AspNetCore.SignalR.Client;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PixelHelper;
public record ChatContact(int Id, string FullName, string Username, bool IsActive, string? Branch, int Unread, long? LastId, bool IsOnline, DateTime? LastAt = null, string? LastText = null, bool IsGroup = false, int? OwnerId = null)
{
    public string Label => (IsGroup ? "👥 " : "") + FullName;
    public string DirectoryLabel => FullName + " (" + Username + ")";
    public string Subtitle => LastText == null ? IsGroup ? IsActive ? "Группа" : "Группа закрыта" : (IsOnline ? "● Online" : "○ Offline") + (Branch == null ? "" : " · " + Branch) : HelperEmojis.PlainText(LastText).Replace('\n', ' ');
    public string TimeLabel => LastAt?.ToLocalTime().ToString("dd.MM HH:mm") ?? "";
    public bool HasUnread => Unread > 0;
    public override string ToString() => (IsGroup ? "👥 " : "") + FullName + (Unread > 0 ? "  ● " + Unread : "") + "\n" + (LastText == null ? IsGroup ? "Группа" : IsOnline ? "● Online" : "○ Offline" : LastText[..Math.Min(50, LastText.Length)].Replace('\n', ' '));
    public static IEnumerable<ChatContact> Ordered(IEnumerable<ChatContact> contacts) => contacts.OrderByDescending(u => u.Unread > 0).ThenByDescending(u => u.LastAt).ThenBy(u => u.FullName);
}
public record ChatAttachment(string Id, string Name, long Size)
{
    public bool IsImage => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" }.Contains(Path.GetExtension(Name).ToLowerInvariant());
    public string Label => Name + " · " + (Size >= 1024 * 1024 ? (Size / 1048576d).ToString("0.0") + " МБ" : (Size / 1024d).ToString("0.0") + " КБ");
}
public record ChatEntry(long Id, int SenderId, int RecipientId, string Body, string ClientId, DateTime SentAt, DateTime? ReadAt, string? SenderName = null, List<ChatAttachment>? Attachments = null);
internal record ChatMember(int Id, string FullName, string Username, bool IsActive);
internal record ChatGroupInfo(int Id, string Name, int OwnerId, bool IsClosed, List<ChatMember> Members);
internal record ChatSession(string Token, int Id, string FullName, string Server);
internal sealed class MessengerClient : IAsyncDisposable
{
    private readonly HttpClient http;
    private readonly HttpClient windowsHttp;
    private readonly string server;
    private readonly string sessionFile = Path.Combine(Settings.Folder, "messenger-session.dat");
    private readonly SemaphoreSlim connectLock = new(1, 1);
    private readonly SemaphoreSlim loginLock = new(1, 1);
    private DateTime renewAt, nextAttempt;
    private readonly CancellationTokenSource lifetime = new();
    private HubConnection? connection;
    private ChatSession? session;
    internal bool SignedIn => session != null;
    internal int UserId => session?.Id ?? 0;
    internal string FullName => session?.FullName ?? "";
    internal string IdentityContext => server + ":" + UserId;
    internal string SignInStatus { get; private set; } = "Подключение под текущей учётной записью Windows…";
    internal event Action<ChatEntry>? MessageReceived;
    internal event Action? Changed;
    internal MessengerClient(Settings settings)
    {
        var uri = new Uri(settings.ServerUrl ?? "https://helper.gp1.loc");
        // Passwords and session tokens must never travel over HTTP.
        if (uri.Scheme == "http") uri = new UriBuilder(uri) { Scheme = "https", Port = uri.IsDefaultPort ? 443 : uri.Port }.Uri;
        if (uri.Scheme != "https" || uri.UserInfo.Length > 0) throw new ArgumentException("Для мессенджера нужен HTTPS.");
        server = uri.AbsoluteUri.TrimEnd('/') + "/";
        filesHttp.BaseAddress = new Uri(server);
        http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(server), Timeout = TimeSpan.FromSeconds(20) };
        // Dedicated handler: Windows credentials are sent only to this server's SSO endpoint.
        windowsHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseDefaultCredentials = true }) { BaseAddress = new Uri(server), Timeout = TimeSpan.FromSeconds(20) };
    }
    internal async Task StartAsync()
    {
        try
        {
            // Discard sessions from the old manual login. Never inherit another AD login.
            File.Delete(sessionFile);
            await SignInWindowsAsync();
        }
        catch (Exception ex) { Settings.Log(ex); }
        _ = Task.Run(async () =>
        {
            while (!lifetime.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(30), lifetime.Token); if (!SignedIn || DateTime.UtcNow >= renewAt) await SignInWindowsAsync(); if (SignedIn) { await Request<object>("api/messenger/me"); await ConnectAsync(); Changed?.Invoke(); } }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                catch (Exception ex) { Settings.Log(ex); }
            }
        });
        Changed?.Invoke();
    }
    internal async Task SignInWindowsAsync(bool force = false)
    {
        if (!force && DateTime.UtcNow < nextAttempt || !await loginLock.WaitAsync(0, lifetime.Token)) return;
        try
        {
            nextAttempt = DateTime.UtcNow.AddMinutes(1);
            SignInStatus = "Подключение под текущей учётной записью Windows…"; Changed?.Invoke();
            using var response = await windowsHttp.GetAsync("api/messenger/windows", lifetime.Token);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(response.StatusCode == HttpStatusCode.Unauthorized ? "Не удалось подтвердить пользователя Windows. Нужны вход в домен AD, доступ к контроллеру и адрес https://helper.gp1.loc." : "Автовход недоступен. Проверьте доступность сервера и настройки AD.");
            var login = await response.Content.ReadFromJsonAsync<ChatSession>(Settings.Json, lifetime.Token) ?? throw new InvalidDataException();
            await connectLock.WaitAsync(lifetime.Token);
            try
            {
                if (connection != null) { await connection.DisposeAsync(); connection = null; }
                session = login with { Server = server }; SetToken(); renewAt = DateTime.UtcNow.AddHours(6);
            }
            finally { connectLock.Release(); }
            SignInStatus = "Вход выполнен: " + FullName;
            await ConnectAsync();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { SignInStatus = ex is InvalidOperationException ? ex.Message : "Нет связи с сервером. Автоматическая повторная попытка через минуту."; Settings.Log(ex); }
        finally { loginLock.Release(); Changed?.Invoke(); }
    }
    private void SetToken() { http.DefaultRequestHeaders.Authorization = session == null ? null : new AuthenticationHeaderValue("Bearer", session.Token); filesHttp.DefaultRequestHeaders.Authorization = http.DefaultRequestHeaders.Authorization; }
    private async Task ConnectAsync()
    {
        if (!await connectLock.WaitAsync(0, lifetime.Token)) return;
        try
        {
            if (!SignedIn) return;
            if (connection == null)
            {
                connection = new HubConnectionBuilder().WithUrl(server + "messengerHub", options => options.AccessTokenProvider = () => Task.FromResult<string?>(session?.Token)).WithAutomaticReconnect().Build();
                connection.On<ChatEntry>("ChatMessage", message => MessageReceived?.Invoke(message));
                connection.On("ChatChanged", () => Changed?.Invoke());
                connection.Reconnected += _ => { Changed?.Invoke(); return Task.CompletedTask; };
                connection.Closed += _ => { Changed?.Invoke(); return Task.CompletedTask; };
            }
            if (connection.State == HubConnectionState.Disconnected) await connection.StartAsync(lifetime.Token);
        }
        finally { connectLock.Release(); }
    }
    private async Task<T> Request<T>(string path, object? body = null)
    {
        using var response = body == null ? await http.GetAsync(path, lifetime.Token) : await http.PostAsJsonAsync(path, body, lifetime.Token);
        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden && path == "api/messenger/me")
        {
            session = null; SetToken(); SignInStatus = "Сеанс завершён. Повторяю вход Windows автоматически…"; Changed?.Invoke(); throw new InvalidOperationException(SignInStatus);
        }
        if (!response.IsSuccessStatusCode)
        {
            string message = "Сервер недоступен. Сообщение можно отправить повторно.";
            try { using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); if (error.RootElement.TryGetProperty("error", out var text)) message = text.GetString() ?? message; } catch (JsonException) { }
            throw new InvalidOperationException(message);
        }
        return (await response.Content.ReadFromJsonAsync<T>(Settings.Json, lifetime.Token))!;
    }
    internal Task<List<ChatContact>> UsersAsync() => Request<List<ChatContact>>("api/messenger/users");
    internal Task<List<ChatEntry>> HistoryAsync(int peer, long? before = null) => Request<List<ChatEntry>>("api/messenger/history/" + peer + (before == null ? "" : "?before=" + before));
    internal Task<ChatEntry> SendAsync(int peer, string text, string clientId) => Request<ChatEntry>("api/messenger/send", new { recipientId = peer, body = text, clientId });
    internal async Task<ChatEntry> SendFilesAsync(int peer, string text, string clientId, IEnumerable<string> paths)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(peer.ToString()), "peer"); form.Add(new StringContent(text), "body"); form.Add(new StringContent(clientId), "clientId");
        foreach (string path in paths) form.Add(new StreamContent(File.OpenRead(path)), "files", Path.GetFileName(path));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); cancellation.CancelAfter(TimeSpan.FromMinutes(5));
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/messenger/files/send") { Content = form };
        using var response = await filesHttp.SendAsync(message, cancellation.Token);
        if (!response.IsSuccessStatusCode) { string error = "Не удалось отправить вложения. Повторите попытку."; try { var data = await response.Content.ReadFromJsonAsync<JsonElement>(Settings.Json, cancellation.Token); if (data.TryGetProperty("error", out var detail)) error = detail.GetString() ?? error; } catch (JsonException) { } throw new InvalidOperationException(error); }
        return (await response.Content.ReadFromJsonAsync<ChatEntry>(Settings.Json, cancellation.Token))!;
    }
    private readonly HttpClient filesHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    internal async Task DownloadAsync(ChatAttachment file, string destination)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); cancellation.CancelAfter(TimeSpan.FromMinutes(5));
        using var response = await filesHttp.GetAsync("api/messenger/files/" + Uri.EscapeDataString(file.Id), HttpCompletionOption.ResponseHeadersRead, cancellation.Token); response.EnsureSuccessStatusCode();
        string temp = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellation.Token))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            { byte[] buffer = new byte[65536]; long total = 0; int count; while ((count = await source.ReadAsync(buffer, cancellation.Token)) > 0) { total += count; if (total > 50 * 1024 * 1024) throw new InvalidDataException("Файл превышает 50 МБ."); await output.WriteAsync(buffer.AsMemory(0, count), cancellation.Token); } }
            File.Move(temp, destination, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal Task<ChatGroupInfo> GroupAsync(int peer) => Request<ChatGroupInfo>("api/messenger/groups/" + -peer);
    internal Task<JsonElement> CreateGroupAsync(string name, int[] members) => Request<JsonElement>("api/messenger/groups", new { name, members });
    internal async Task GroupOperationAsync(int peer, string operation, object? body = null)
    {
        using var response = await http.PostAsJsonAsync($"api/messenger/groups/{-peer}/{operation}", body ?? new { }, lifetime.Token);
        if (!response.IsSuccessStatusCode)
        {
            string error = "Не удалось изменить группу.";
            try { using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); if (json.RootElement.TryGetProperty("error", out var value)) error = value.GetString() ?? error; } catch (JsonException) { }
            throw new InvalidOperationException(error);
        }
        Changed?.Invoke();
    }
    internal async Task ReadAsync(int peer, long through)
    {
        using var response = await http.PostAsJsonAsync($"api/messenger/read/{peer}/{through}", new { }, lifetime.Token); response.EnsureSuccessStatusCode();
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); if (connection != null) await connection.DisposeAsync(); http.Dispose(); windowsHttp.Dispose(); filesHttp.Dispose();
    }
}
