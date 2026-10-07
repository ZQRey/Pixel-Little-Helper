using Microsoft.AspNetCore.SignalR.Client;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PixelHelper;
public record ChatContact(int Id, string FullName, string Username, bool IsActive, string? Branch, int Unread, long? LastId, bool IsOnline)
{
    public override string ToString() => FullName + (Unread > 0 ? "  (" + Unread + ")" : "") + "\n" + (IsOnline ? "● Online" : "○ Offline") + (Branch == null ? "" : " · " + Branch);
}
public record ChatEntry(long Id, int SenderId, int RecipientId, string Body, string ClientId, DateTime SentAt, DateTime? ReadAt);
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
    private void SetToken() => http.DefaultRequestHeaders.Authorization = session == null ? null : new AuthenticationHeaderValue("Bearer", session.Token);
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
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
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
    internal async Task ReadAsync(int peer, long through)
    {
        using var response = await http.PostAsJsonAsync($"api/messenger/read/{peer}/{through}", new { }, lifetime.Token); response.EnsureSuccessStatusCode();
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); if (connection != null) await connection.DisposeAsync(); http.Dispose(); windowsHttp.Dispose();
    }
}
