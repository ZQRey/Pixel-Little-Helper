using Microsoft.AspNetCore.SignalR.Client;

namespace PixelHelper;

public sealed class HubConnectionService : IAsyncDisposable
{
    private readonly Settings settings;
    private readonly HubConnection connection;
    private readonly CancellationTokenSource lifetime = new();
    private readonly CommandExecutor executor;
    private readonly SemaphoreSlim inventoryLock = new(1, 1);
    private Task? loop;
    private bool registered;
    public bool IsOnline => registered && connection.State == HubConnectionState.Connected;
    public event Action<bool>? OnlineChanged;
    public event Action<List<ActionButton>>? ButtonsUpdated;
    public HubConnectionService(Settings settings)
    {
        this.settings = settings; executor = new(settings);
        string url = settings.HubUrl ?? (string.IsNullOrWhiteSpace(settings.ServerUrl) ? "http://helper-server" : settings.ServerUrl.TrimEnd('/')) + "/helperHub";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new ArgumentException("Неверный адрес хаба");
        connection = new HubConnectionBuilder().WithUrl(uri, options =>
        {
            options.Headers["X-Client-Key"] = settings.ClientToken;
            options.Headers["X-Machine-Name"] = Environment.MachineName;
        }).WithAutomaticReconnect().Build();
        connection.On<List<ActionButton>>("OnButtonsUpdated", buttons => { if (registered) ButtonsUpdated?.Invoke(buttons); });
        connection.On<CommandEnvelope>("ExecuteCommand", RunTask);
        connection.On("RefreshInventory", SendInventory);
        connection.On<string>("KillProcess", name => RunTask(new(Guid.NewGuid().ToString("N"), "kill", name)));
        connection.On("Reboot", () => RunTask(new(Guid.NewGuid().ToString("N"), "reboot", "")));
        connection.On("Shutdown", () => RunTask(new(Guid.NewGuid().ToString("N"), "shutdown", "")));
        connection.Reconnecting += _ => { Offline(); return Task.CompletedTask; };
        connection.Closed += _ => { Offline(); return Task.CompletedTask; };
        connection.Reconnected += async _ => { try { await Register(); } catch (Exception ex) { Settings.Log(ex); Offline(); } };
    }
    private void Offline() { registered = false; OnlineChanged?.Invoke(false); }
    public void Start() => loop ??= ConnectLoop();
    private async Task ConnectLoop()
    {
        if (string.IsNullOrWhiteSpace(settings.ClientToken)) { Offline(); return; }
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                if (connection.State == HubConnectionState.Disconnected) { await connection.StartAsync(lifetime.Token); await Register(); }
                else if (connection.State == HubConnectionState.Connected)
                {
                    if (!registered) await Register(); else await connection.InvokeAsync("Heartbeat", lifetime.Token);
                }
                await Task.Delay(TimeSpan.FromSeconds(30), lifetime.Token);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Settings.Log(ex); Offline();
                try { await Task.Delay(TimeSpan.FromSeconds(15), lifetime.Token); } catch (OperationCanceledException) { break; }
            }
        }
    }
    private async Task Register()
    {
        var machine = await Task.Run(SystemInspector.Machine, lifetime.Token);
        var buttons = await connection.InvokeAsync<List<ActionButton>>("RegisterComputer", machine, lifetime.Token);
        registered = true; OnlineChanged?.Invoke(true); ButtonsUpdated?.Invoke(buttons);
        // Inventory does not block button availability or the WPF dispatcher.
        await SendInventory();
    }
    public async Task RefreshButtons()
    {
        if (!IsOnline) return;
        var buttons = await connection.InvokeAsync<List<ActionButton>>("RegisterComputer", await Task.Run(SystemInspector.Machine), lifetime.Token);
        ButtonsUpdated?.Invoke(buttons);
    }
    private async Task<bool> SendInventory()
    {
        if (!IsOnline || !await inventoryLock.WaitAsync(0, lifetime.Token)) return false;
        try
        {
            var snapshot = await Task.Run(SystemInspector.Collect, lifetime.Token);
            await connection.InvokeAsync("UpdateHardwareAndSoftware", snapshot.Hardware, snapshot.Software, lifetime.Token);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Settings.Log(ex); return false; }
        finally { inventoryLock.Release(); }
    }
    private async Task RunTask(CommandEnvelope task)
    {
        if (!registered) return;
        try
        {
            (string Output, int ExitCode) result;
            if (task.Type == "inventory") { bool updated = await SendInventory(); result = updated ? ("Инвентаризация обновлена", 0) : ("Инвентаризация не отправлена: ошибка связи или предыдущий сбор ещё выполняется", 1); }
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
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); Offline();
        await connection.DisposeAsync();
        if (loop != null) { try { await loop; } catch (OperationCanceledException) { } }
        // WMI may still be returning from a bounded query; locks stay alive until callbacks finish.
        lifetime.Dispose();
    }
}
