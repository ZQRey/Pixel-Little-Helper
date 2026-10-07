using Microsoft.AspNetCore.SignalR.Client;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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
    private readonly string server;
    private readonly string sessionFile = Path.Combine(Settings.Folder, "messenger-session.dat");
    private readonly SemaphoreSlim connectLock = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private HubConnection? connection;
    private ChatSession? session;
    internal bool SignedIn => session != null;
    internal int UserId => session?.Id ?? 0;
    internal string FullName => session?.FullName ?? "";
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
    }
    internal async Task StartAsync()
    {
        try
        {
            if (File.Exists(sessionFile))
            {
                session = JsonSerializer.Deserialize<ChatSession>(ProtectedData.Unprotect(File.ReadAllBytes(sessionFile), Encoding.UTF8.GetBytes(server), DataProtectionScope.CurrentUser), Settings.Json);
                if (session?.Server != server) session = null;
                if (session != null) { SetToken(); await Request<object>("api/messenger/me"); await ConnectAsync(); }
            }
        }
        catch (Exception ex) { Settings.Log(ex); }
        _ = Task.Run(async () =>
        {
            while (!lifetime.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(30), lifetime.Token); if (SignedIn) { await Request<object>("api/messenger/me"); await ConnectAsync(); Changed?.Invoke(); } }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                catch (Exception ex) { Settings.Log(ex); }
            }
        });
        Changed?.Invoke();
    }
    internal async Task LoginAsync(string username, string password)
    {
        using var response = await http.PostAsJsonAsync("api/messenger/login", new { username, password }, lifetime.Token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(response.StatusCode == HttpStatusCode.Unauthorized ? "Неверный логин или пароль AD." : "Не удалось войти. Проверьте доступность AD и HTTPS.");
        var login = await response.Content.ReadFromJsonAsync<ChatSession>(Settings.Json, lifetime.Token) ?? throw new InvalidDataException();
        session = login with { Server = server }; SetToken();
        Directory.CreateDirectory(Settings.Folder);
        File.WriteAllBytes(sessionFile, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(session, Settings.Json), Encoding.UTF8.GetBytes(server), DataProtectionScope.CurrentUser));
        await ConnectAsync(); Changed?.Invoke();
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
            session = null; SetToken(); File.Delete(sessionFile); Changed?.Invoke(); throw new InvalidOperationException("Войдите в мессенджер через AD заново.");
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
    internal async Task LogoutAsync()
    {
        session = null; SetToken(); File.Delete(sessionFile);
        if (connection != null) { await connection.DisposeAsync(); connection = null; }
        Changed?.Invoke();
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); if (connection != null) await connection.DisposeAsync(); http.Dispose();
    }
}
