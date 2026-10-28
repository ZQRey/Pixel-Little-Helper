using System.Collections.Concurrent;

namespace PixelHelper;

public sealed class MailBackgroundWorker : IDisposable
{
    private readonly Settings settings;
    private readonly MailStorageEngine storage;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> accountWorkers = new(StringComparer.OrdinalIgnoreCase);
    private Task? supervisorLoop;

    public MailBackgroundWorker(Settings settings, MailStorageEngine storage)
    {
        this.settings = settings;
        this.storage = storage;
    }

    public void Start()
    {
        supervisorLoop ??= Task.Run(RunSupervisorAsync);
    }

    private async Task RunSupervisorAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                var enabledAccounts = settings.MailAccounts
                    .Where(a => a.IsEnabled && !string.IsNullOrWhiteSpace(a.IncomingHost))
                    .ToList();

                var activeIds = enabledAccounts.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Stop workers for removed or disabled accounts
                foreach (var kvp in accountWorkers.ToArray())
                {
                    if (!activeIds.Contains(kvp.Key))
                    {
                        if (accountWorkers.TryRemove(kvp.Key, out var cts))
                        {
                            try { cts.Cancel(); cts.Dispose(); } catch { }
                        }
                    }
                }

                // Start dedicated real-time worker for each enabled account
                foreach (var account in enabledAccounts)
                {
                    if (!accountWorkers.ContainsKey(account.Id))
                    {
                        var accountCts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        if (accountWorkers.TryAdd(account.Id, accountCts))
                        {
                            string accountId = account.Id;
                            _ = Task.Run(() => RunAccountRealtimeLoopAsync(accountId, accountCts.Token));
                        }
                        else
                        {
                            accountCts.Dispose();
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunAccountRealtimeLoopAsync(string accountId, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var account = settings.MailAccounts.FirstOrDefault(a => a.Id == accountId && a.IsEnabled);
                if (account == null || string.IsNullOrWhiteSpace(account.IncomingHost))
                    break;

                // 1. Sync new messages immediately
                try
                {
                    await storage.SyncAccountAsync(account, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Settings.Log(ex);
                }

                // 2. Listen via IMAP IDLE (real-time push) or wait 10s for next automatic check
                if (account.IncomingProtocol == MailIncomingProtocol.Imap)
                {
                    string plainPassword = MailCryptoService.UnprotectSecret(account.EncryptedPassword);
                    await MailProtocolClient.WaitImapIdleAsync(account, plainPassword, TimeSpan.FromSeconds(10), token);
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
        finally
        {
            if (accountWorkers.TryRemove(accountId, out var cts))
            {
                try { cts.Dispose(); } catch { }
            }
        }
    }

    public void Dispose()
    {
        try { lifetime.Cancel(); } catch { }
        foreach (var kvp in accountWorkers.ToArray())
        {
            try { kvp.Value.Cancel(); kvp.Value.Dispose(); } catch { }
        }
        accountWorkers.Clear();
        lifetime.Dispose();
    }
}
