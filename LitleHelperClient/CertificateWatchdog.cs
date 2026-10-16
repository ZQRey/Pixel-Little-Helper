using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Threading;

namespace PixelHelper;

public sealed record CertificateStatusInfo(
    string Subject,
    string Issuer,
    string Thumbprint,
    DateTime NotBefore,
    DateTime NotAfter,
    int DaysRemaining,
    bool IsNucRk,
    bool IsExpired);

public sealed class CertificateWatchdog : IDisposable
{
    private readonly DispatcherTimer timer = new();
    private readonly CancellationTokenSource lifetime = new();
    private int checking;
    private readonly Action<string>? showNotice;
    private DateTime lastAlert = DateTime.MinValue;
    private static readonly TimeSpan AlertCooldown = TimeSpan.FromHours(12);

    public CertificateWatchdog(Action<string>? showNotice = null, TimeSpan? interval = null)
    {
        this.showNotice = showNotice;
        timer.Interval = interval ?? TimeSpan.FromHours(4);
        timer.Tick += async (_, _) => await CheckAsync();
    }

    public async void Start(TimeSpan? initialDelay = null)
    {
        try {
            await Task.Delay(initialDelay ?? TimeSpan.FromSeconds(45), lifetime.Token);
            if (lifetime.IsCancellationRequested) return;
            await CheckAsync();
            if (!lifetime.IsCancellationRequested) timer.Start();
        } catch (OperationCanceledException) { }
    }
    private async Task CheckAsync()
    {
        if (lifetime.IsCancellationRequested || Interlocked.Exchange(ref checking,1) != 0) return;
        try { await Task.Run(CheckCertificates); }
        catch (Exception ex) { Settings.Log(ex); }
        finally { Interlocked.Exchange(ref checking,0); }
    }
    public void Stop() { lifetime.Cancel(); timer.Stop(); }
    public void Dispose() => Stop();

    public static List<CertificateStatusInfo> GetInstalledCertificates()
    {
        var list = new List<CertificateStatusInfo>();
        var locations = new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine };

        foreach (var loc in locations)
        {
            try
            {
                using var store = new X509Store(StoreName.My, loc);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                foreach (var cert in store.Certificates)
                {
                    try
                    {
                        string subject = cert.GetNameInfo(X509NameType.SimpleName, false) ?? cert.Subject;
                        string issuer = cert.GetNameInfo(X509NameType.SimpleName, true) ?? cert.Issuer;
                        bool isNucRk = issuer.Contains("НУЦ", StringComparison.OrdinalIgnoreCase) ||
                                       issuer.Contains("НИТ", StringComparison.OrdinalIgnoreCase) ||
                                       issuer.Contains("NCA", StringComparison.OrdinalIgnoreCase) ||
                                       issuer.Contains("pki.gov.kz", StringComparison.OrdinalIgnoreCase) ||
                                       subject.Contains("IIN", StringComparison.OrdinalIgnoreCase) ||
                                       subject.Contains("BIN", StringComparison.OrdinalIgnoreCase);

                        int daysRemaining = (int)(cert.NotAfter.ToUniversalTime() - DateTime.UtcNow).TotalDays;
                        bool isExpired = daysRemaining < 0;

                        list.Add(new(
                            Subject: subject,
                            Issuer: issuer,
                            Thumbprint: cert.Thumbprint,
                            NotBefore: cert.NotBefore,
                            NotAfter: cert.NotAfter,
                            DaysRemaining: daysRemaining,
                            IsNucRk: isNucRk,
                            IsExpired: isExpired));
                    }
                    catch { }
                }
            }
            catch { }
        }

        return list.DistinctBy(c => c.Thumbprint).ToList();
    }

    public static (bool Success, string Message) ImportP12Certificate(string filePath, string password)
    {
        try
        {
            if (!File.Exists(filePath)) return (false, "Файл сертификата не найден.");

            var cert = new X509Certificate2(filePath, password, X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(cert);

            string name = cert.GetNameInfo(X509NameType.SimpleName, false) ?? cert.Subject;
            return (true, $"Сертификат \"{name}\" успешно установлен в личное хранилище Windows!");
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
            return (false, "Ошибка импорта сертификата: неверный пароль или поврежденный файл.");
        }
    }

    public void CheckCertificates()
    {
        try
        {
            var certs = GetInstalledCertificates();
            var nucCerts = certs.Where(c => c.IsNucRk).ToList();

            foreach (var cert in nucCerts)
            {
                if (cert.DaysRemaining is <= 14 and >= 0)
                {
                    if (DateTime.UtcNow - lastAlert >= AlertCooldown)
                    {
                        lastAlert = DateTime.UtcNow;
                        string notice = cert.DaysRemaining == 0
                            ? $"Внимание: срок действия ЭЦП \"{cert.Subject}\" истекает СЕГОДНЯ! Пожалуйста, продлите сертификат на pki.gov.kz 🔑"
                            : $"Срок действия ключа ЭЦП \"{cert.Subject}\" истекает через {cert.DaysRemaining} дн. ({cert.NotAfter:dd.MM.yyyy}). Продлите сертификат на pki.gov.kz 🔑";
                        if (!lifetime.IsCancellationRequested) timer.Dispatcher.InvokeAsync(() => { if (!lifetime.IsCancellationRequested) showNotice?.Invoke(notice); });
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }
}
