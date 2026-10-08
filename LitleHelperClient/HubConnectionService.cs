using Microsoft.AspNetCore.SignalR.Client;
using System.Net.Http;
using System.Net.Http.Json;

namespace PixelHelper;

public sealed class HubConnectionService : IAsyncDisposable
{
    private readonly Settings settings;
    private HubConnection connection;
    private readonly CancellationTokenSource lifetime = new();
    private readonly CommandExecutor executor;
    private readonly SemaphoreSlim inventoryLock = new(1, 1);
    private Task? loop;
    private volatile bool registered;
    private bool registrationSent;
    public bool IsOnline => registered && connection.State == HubConnectionState.Connected;
    public event Action<bool>? OnlineChanged;
    public event Action<List<ActionButton>>? ButtonsUpdated;
    public event Action<bool>? SuperAdminAvailable;
    public event Func<ClientNotice, Task<string>>? NoticeReceived;
    public HubConnectionService(Settings settings)
    {
        this.settings = settings; executor = new(settings);
        if (settings.EnsureClientKey())
        {
            settings.Save(); // Persist before sending; retry after restart uses the same identity.
        }
        connection = BuildConnection();
    }
    public string Status { get; private set; } = "Подключение";
    public DateTime? LastSuccess { get; private set; }
    private void Stage(string status)
    {
        Status = status;
        try { System.IO.Directory.CreateDirectory(Settings.Folder); string path = System.IO.Path.Combine(Settings.Folder, "connection.log"); if (System.IO.File.Exists(path) && new System.IO.FileInfo(path).Length > 512000) System.IO.File.Delete(path); System.IO.File.AppendAllText(path, DateTime.UtcNow.ToString("u") + " " + status + "\n"); } catch { }
    }
    private HubConnection BuildConnection()
    {
        string url = settings.HubUrl ?? (string.IsNullOrWhiteSpace(settings.ServerUrl) ? "http://helper.gp1.loc" : settings.ServerUrl.TrimEnd('/')) + "/helperHub";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new ArgumentException("Неверный адрес хаба");
        var created = new HubConnectionBuilder().WithUrl(uri, options =>
        {
            options.Headers["X-Client-Key"] = settings.ClientToken;
            options.Headers["X-Machine-Name"] = Environment.MachineName;
        }).Build();
        created.On<List<ActionButton>>("OnButtonsUpdated", buttons => { if (registered) ButtonsUpdated?.Invoke(buttons); });
        created.On<CommandEnvelope>("ExecuteCommand", RunTask);
        created.On<bool>("SuperAdminAvailable", available => SuperAdminAvailable?.Invoke(available));
        created.On("RefreshInventory", SendInventory);
        created.On<string>("KillProcess", name => RunTask(new(Guid.NewGuid().ToString("N"), "kill", name)));
        created.On("Reboot", () => RunTask(new(Guid.NewGuid().ToString("N"), "reboot", "")));
        created.On("Shutdown", () => RunTask(new(Guid.NewGuid().ToString("N"), "shutdown", "")));
        created.Reconnecting += _ => { Offline(); return Task.CompletedTask; };
        created.Closed += _ => { if (ReferenceEquals(connection,created)) Offline(); return Task.CompletedTask; };

        created.ServerTimeout = TimeSpan.FromSeconds(25); created.HandshakeTimeout = TimeSpan.FromSeconds(10); created.KeepAliveInterval = TimeSpan.FromSeconds(10);
        return created;
    }
    private void Offline() { registered = false; SuperAdminAvailable?.Invoke(false); OnlineChanged?.Invoke(false); }
    public void Start() => loop ??= Task.Run(ConnectLoop);
    private async Task ConnectLoop()
    {
        if (string.IsNullOrWhiteSpace(settings.ClientToken)) { Offline(); return; }
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                string? machineKey = MachineIdentity.Read(settings.ServerUrl);
                if (machineKey != null && machineKey != settings.ClientToken) { settings.ClientToken = machineKey; settings.Save(); await connection.DisposeAsync(); connection = BuildConnection(); registrationSent = false; registered = false; }
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); attempt.CancelAfter(TimeSpan.FromSeconds(25));
                if (connection.State == HubConnectionState.Disconnected)
                {
                    Stage("Регистрация клиента");
                    if (!registrationSent)
                    {
                        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
                        var endpoint = new Uri(settings.ServerUrl!.TrimEnd('/') + "/api/agents/register");
                        using var response = await http.PostAsJsonAsync(endpoint, new { machineName = Environment.MachineName, clientKey = settings.ClientToken }, lifetime.Token);
                        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                            throw new InvalidOperationException("Компьютер уже зарегистрирован с другим ключом. Обратитесь к администратору.");
                        response.EnsureSuccessStatusCode(); registrationSent = true;
                    }
                    Stage("Подключение SignalR"); await connection.StartAsync(attempt.Token); await Register(attempt.Token);
                }
                else if (connection.State == HubConnectionState.Connected)
                {
                    if (!registered) await Register(attempt.Token); else { Stage("Проверка связи"); await connection.InvokeAsync("Heartbeat", attempt.Token); LastSuccess = DateTime.UtcNow; Stage("В сети"); }
                }
                await Task.Delay(TimeSpan.FromSeconds(30), lifetime.Token);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Settings.Log(ex); Offline(); Stage(ex is OperationCanceledException or TimeoutException ? "Нет ответа сервера · повторное подключение" : "Повторное подключение: " + ex.Message);
                try { await connection.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); } catch (Exception stopError) { Settings.Log(stopError); }
                connection = BuildConnection(); registrationSent = false;
                try { await Task.Delay(TimeSpan.FromSeconds(15), lifetime.Token); } catch (OperationCanceledException) { break; }
            }
        }
    }
    private async Task Register(CancellationToken token)
    {
        var machine = await Task.Run(SystemInspector.Machine, token).WaitAsync(TimeSpan.FromSeconds(5), token);
        var buttons = await connection.InvokeAsync<List<ActionButton>>("RegisterComputer", machine, token);
        LastSuccess = DateTime.UtcNow; Stage("В сети"); registered = true; OnlineChanged?.Invoke(true); ButtonsUpdated?.Invoke(buttons);
        // Inventory does not block button availability or the WPF dispatcher.
        _ = SendInventory();
    }
    public async Task RefreshButtons()
    {
        if (!IsOnline) return;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
        var machine=await Task.Run(SystemInspector.Machine).WaitAsync(TimeSpan.FromSeconds(5),timeout.Token);
        var buttons = await connection.InvokeAsync<List<ActionButton>>("RegisterComputer", machine, timeout.Token);
        ButtonsUpdated?.Invoke(buttons);
    }
    private async Task<bool> SendInventory()
    {
        if (!IsOnline || !await inventoryLock.WaitAsync(0, lifetime.Token)) return false;
        try
        {
            var snapshot = await Task.Run(SystemInspector.Collect, lifetime.Token);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
            await connection.InvokeAsync("UpdateHardwareAndSoftware", snapshot.Hardware, snapshot.Software, timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return false; }
        catch (Exception ex) { Settings.Log(ex); return false; }
        finally { inventoryLock.Release(); }
    }
    private async Task RunTask(CommandEnvelope task)
    {
        if (!registered) return;
        try
        {
            (string Output, int ExitCode) result;
            if (task.Type == "notice")
            {
                try
                {
                    var notice = System.Text.Json.JsonSerializer.Deserialize<ClientNotice>(task.Payload, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (notice == null || string.IsNullOrWhiteSpace(notice.Text) || notice.Text.Length > 1000 || notice.DurationSeconds is < 10 or > 300 || NoticeReceived == null)
                        result = ("Сообщение не показано: неверные данные или окно недоступно", 1);
                    else result = (await NoticeReceived(notice), 0);
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { result = ("Сообщение не показано: " + ex.Message, 1); }
            }
            else if (task.Type == "inventory") { bool updated = await SendInventory(); result = updated ? ("Инвентаризация обновлена", 0) : ("Инвентаризация не отправлена: ошибка связи или предыдущий сбор ещё выполняется", 1); }
            else result = await executor.ExecuteAsync(task, async output =>
            {
                try { if (IsOnline) await connection.InvokeAsync("SendExecutionOutput", task.TaskId, output, lifetime.Token); }
                catch (Exception ex) when (ex is not OperationCanceledException) { Settings.Log(ex); }
            }, lifetime.Token);
            if (IsOnline) await connection.InvokeAsync("SendExecutionResult", task.TaskId, result.Output, result.ExitCode, lifetime.Token);
        }
        catch (Exception ex) { Settings.Log(ex); }
    }
    public async Task<int> CreateTicketAsync(string title, string description, CancellationToken token)
    {
        if (!IsOnline) throw new InvalidOperationException("Сервер недоступен. Заявка не отправлена.");
        return await connection.InvokeAsync<int>("CreateTicket", title, description, token);
    }
    public async Task<List<TicketBranch>> GetBranchesAsync(CancellationToken token)
    {
        if (!IsOnline) throw new InvalidOperationException("Сервер недоступен.");
        return await connection.InvokeAsync<List<TicketBranch>>("GetBranches", token);
    }
    public async Task<int> CreateTicketAtAsync(string title, string description, int? branchId, string room, CancellationToken token)
    {
        if (!IsOnline) throw new InvalidOperationException("Сервер недоступен. Заявка не отправлена.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        return await connection.InvokeAsync<int>("CreateTicketAt", title, description, branchId, room, timeout.Token);
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); Offline();
        try { await connection.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); } catch (Exception ex) { Settings.Log(ex); }
        if (loop != null) { try { await loop.WaitAsync(TimeSpan.FromSeconds(10)); } catch (Exception ex) { Settings.Log(ex); } }
        // WMI may still be returning from a bounded query; locks stay alive until callbacks finish.
        lifetime.Dispose();
    }
}
