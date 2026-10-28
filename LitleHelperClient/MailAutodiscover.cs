using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace PixelHelper;

public sealed record MailAutodiscoverResult(
    bool Success,
    string Source,
    MailIncomingProtocol IncomingProtocol,
    string IncomingHost,
    int IncomingPort,
    MailSslMode IncomingSslMode,
    string SmtpHost,
    int SmtpPort,
    MailSslMode SmtpSslMode,
    string Message
);

public static class MailAutodiscover
{
    private sealed record ProviderPreset(
        MailIncomingProtocol Protocol,
        string IncomingHost,
        int IncomingPort,
        MailSslMode IncomingSsl,
        string SmtpHost,
        int SmtpPort,
        MailSslMode SmtpSsl);

    private static readonly Dictionary<string, ProviderPreset> KnownProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["yandex.ru"] = new(MailIncomingProtocol.Imap, "imap.yandex.ru", 993, MailSslMode.ImplicitSsl, "smtp.yandex.ru", 465, MailSslMode.ImplicitSsl),
        ["ya.ru"] = new(MailIncomingProtocol.Imap, "imap.yandex.ru", 993, MailSslMode.ImplicitSsl, "smtp.yandex.ru", 465, MailSslMode.ImplicitSsl),
        ["yandex.kz"] = new(MailIncomingProtocol.Imap, "imap.yandex.kz", 993, MailSslMode.ImplicitSsl, "smtp.yandex.kz", 465, MailSslMode.ImplicitSsl),
        ["yandex.com"] = new(MailIncomingProtocol.Imap, "imap.yandex.com", 993, MailSslMode.ImplicitSsl, "smtp.yandex.com", 465, MailSslMode.ImplicitSsl),
        ["mail.ru"] = new(MailIncomingProtocol.Imap, "imap.mail.ru", 993, MailSslMode.ImplicitSsl, "smtp.mail.ru", 465, MailSslMode.ImplicitSsl),
        ["inbox.ru"] = new(MailIncomingProtocol.Imap, "imap.mail.ru", 993, MailSslMode.ImplicitSsl, "smtp.mail.ru", 465, MailSslMode.ImplicitSsl),
        ["bk.ru"] = new(MailIncomingProtocol.Imap, "imap.mail.ru", 993, MailSslMode.ImplicitSsl, "smtp.mail.ru", 465, MailSslMode.ImplicitSsl),
        ["list.ru"] = new(MailIncomingProtocol.Imap, "imap.mail.ru", 993, MailSslMode.ImplicitSsl, "smtp.mail.ru", 465, MailSslMode.ImplicitSsl),
        ["gmail.com"] = new(MailIncomingProtocol.Imap, "imap.gmail.com", 993, MailSslMode.ImplicitSsl, "smtp.gmail.com", 465, MailSslMode.ImplicitSsl),
        ["googlemail.com"] = new(MailIncomingProtocol.Imap, "imap.gmail.com", 993, MailSslMode.ImplicitSsl, "smtp.gmail.com", 465, MailSslMode.ImplicitSsl),
        ["outlook.com"] = new(MailIncomingProtocol.Imap, "outlook.office365.com", 993, MailSslMode.ImplicitSsl, "smtp.office365.com", 587, MailSslMode.StartTls),
        ["hotmail.com"] = new(MailIncomingProtocol.Imap, "outlook.office365.com", 993, MailSslMode.ImplicitSsl, "smtp.office365.com", 587, MailSslMode.StartTls),
        ["office365.com"] = new(MailIncomingProtocol.Imap, "outlook.office365.com", 993, MailSslMode.ImplicitSsl, "smtp.office365.com", 587, MailSslMode.StartTls),
        ["rambler.ru"] = new(MailIncomingProtocol.Imap, "imap.rambler.ru", 993, MailSslMode.ImplicitSsl, "smtp.rambler.ru", 465, MailSslMode.ImplicitSsl),
        ["icloud.com"] = new(MailIncomingProtocol.Imap, "imap.mail.me.com", 993, MailSslMode.ImplicitSsl, "smtp.mail.me.com", 587, MailSslMode.StartTls)
    };

    public static async Task<MailAutodiscoverResult> DiscoverAsync(string emailAddress, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(emailAddress) || !emailAddress.Contains('@'))
        {
            return new MailAutodiscoverResult(
                false, "Validation", MailIncomingProtocol.Imap, "", 993, MailSslMode.ImplicitSsl, "", 465, MailSslMode.ImplicitSsl,
                "Введите корректный Email адрес (например: user@gp1.loc).");
        }

        string domain = emailAddress.Split('@').Last().Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(domain))
        {
            return new MailAutodiscoverResult(
                false, "Validation", MailIncomingProtocol.Imap, "", 993, MailSslMode.ImplicitSsl, "", 465, MailSslMode.ImplicitSsl,
                "Не удалось выделить домен из Email адреса.");
        }

        // 1. Check built-in provider presets
        if (KnownProviders.TryGetValue(domain, out var preset))
        {
            return new MailAutodiscoverResult(
                true,
                "KnownProvider",
                preset.Protocol,
                preset.IncomingHost,
                preset.IncomingPort,
                preset.IncomingSsl,
                preset.SmtpHost,
                preset.SmtpPort,
                preset.SmtpSsl,
                $"Автоматически определены параметры провайдера {domain}: {preset.IncomingHost}:{preset.IncomingPort} ({preset.IncomingSsl}), SMTP {preset.SmtpHost}:{preset.SmtpPort}.");
        }

        // 2. Try Mozilla Autoconfig XML if available
        var autoconfig = await TryAutoconfigXmlAsync(domain, token);
        if (autoconfig != null) return autoconfig;

        // 3. Parallel probing of candidate hosts and standard ports (IMAP 993/143, POP3 995/110, SMTP 465/587/25)
        string[] incomingHosts = [$"mail.{domain}", $"imap.{domain}", $"pop.{domain}", domain];
        string[] smtpHosts = [$"mail.{domain}", $"smtp.{domain}", domain];

        var incomingCandidate = await ProbeIncomingEndpointsAsync(incomingHosts, token);
        var smtpCandidate = await ProbeSmtpEndpointsAsync(smtpHosts, token);

        if (incomingCandidate != null && smtpCandidate != null)
        {
            return new MailAutodiscoverResult(
                true,
                "PortProbe",
                incomingCandidate.Value.Protocol,
                incomingCandidate.Value.Host,
                incomingCandidate.Value.Port,
                incomingCandidate.Value.Ssl,
                smtpCandidate.Value.Host,
                smtpCandidate.Value.Port,
                smtpCandidate.Value.Ssl,
                $"Автоматически распознаны порты сервера {domain}: {incomingCandidate.Value.Protocol.ToString().ToUpperInvariant()} {incomingCandidate.Value.Host}:{incomingCandidate.Value.Port} ({incomingCandidate.Value.Ssl}), SMTP {smtpCandidate.Value.Host}:{smtpCandidate.Value.Port} ({smtpCandidate.Value.Ssl}).");
        }

        // 4. Smart fallback for corporate domains (e.g. gp1.loc or internal domains)
        string fallbackHost = $"mail.{domain}";
        return new MailAutodiscoverResult(
            true,
            "CorporateDefault",
            incomingCandidate?.Protocol ?? MailIncomingProtocol.Imap,
            incomingCandidate?.Host ?? fallbackHost,
            incomingCandidate?.Port ?? 993,
            incomingCandidate?.Ssl ?? MailSslMode.ImplicitSsl,
            smtpCandidate?.Host ?? fallbackHost,
            smtpCandidate?.Port ?? 465,
            smtpCandidate?.Ssl ?? MailSslMode.ImplicitSsl,
            $"Подставлены стандартные параметры домена {domain} (IMAP {fallbackHost}:993 SSL, SMTP {fallbackHost}:465 SSL). При необходимости скорректируйте порты.");
    }

    private static async Task<MailAutodiscoverResult?> TryAutoconfigXmlAsync(string domain, CancellationToken token)
    {
        string[] urls =
        [
            $"https://autoconfig.{domain}/mail/config-v1.1.xml",
            $"https://{domain}/.well-known/autoconfig/mail/config-v1.1.xml"
        ];

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2.5) };
        foreach (string url in urls)
        {
            try
            {
                using var resp = await http.GetAsync(url, token);
                if (!resp.IsSuccessStatusCode) continue;
                string xml = await resp.Content.ReadAsStringAsync(token);
                var doc = XDocument.Parse(xml);
                var incoming = doc.Descendants("incomingServer").FirstOrDefault();
                var outgoing = doc.Descendants("outgoingServer").FirstOrDefault();
                if (incoming == null || outgoing == null) continue;

                string inType = incoming.Attribute("type")?.Value ?? "imap";
                string inHost = incoming.Element("hostname")?.Value ?? $"mail.{domain}";
                int inPort = int.TryParse(incoming.Element("port")?.Value, out int ip) ? ip : 993;
                string inSock = incoming.Element("socketType")?.Value ?? "SSL";

                string outHost = outgoing.Element("hostname")?.Value ?? $"smtp.{domain}";
                int outPort = int.TryParse(outgoing.Element("port")?.Value, out int op) ? op : 465;
                string outSock = outgoing.Element("socketType")?.Value ?? "SSL";

                var protocol = inType.Equals("pop3", StringComparison.OrdinalIgnoreCase)
                    ? MailIncomingProtocol.Pop3
                    : MailIncomingProtocol.Imap;

                var inSsl = inSock.Equals("STARTTLS", StringComparison.OrdinalIgnoreCase)
                    ? MailSslMode.StartTls
                    : inSock.Equals("plain", StringComparison.OrdinalIgnoreCase) ? MailSslMode.None : MailSslMode.ImplicitSsl;

                var outSsl = outSock.Equals("STARTTLS", StringComparison.OrdinalIgnoreCase)
                    ? MailSslMode.StartTls
                    : outSock.Equals("plain", StringComparison.OrdinalIgnoreCase) ? MailSslMode.None : MailSslMode.ImplicitSsl;

                return new MailAutodiscoverResult(
                    true, "AutoconfigXml", protocol, inHost, inPort, inSsl, outHost, outPort, outSsl,
                    $"Настройки получены из Autoconfig XML домена {domain}.");
            }
            catch
            {
                // Ignore and continue to port probing
            }
        }
        return null;
    }

    private static async Task<(MailIncomingProtocol Protocol, string Host, int Port, MailSslMode Ssl)?> ProbeIncomingEndpointsAsync(
        string[] hosts,
        CancellationToken token)
    {
        var candidates = new List<(MailIncomingProtocol Protocol, string Host, int Port, MailSslMode Ssl, int Priority)>();
        int prio = 0;
        foreach (string h in hosts.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add((MailIncomingProtocol.Imap, h, 993, MailSslMode.ImplicitSsl, prio++));
            candidates.Add((MailIncomingProtocol.Imap, h, 143, MailSslMode.StartTls, prio++));
            candidates.Add((MailIncomingProtocol.Pop3, h, 995, MailSslMode.ImplicitSsl, prio++));
            candidates.Add((MailIncomingProtocol.Pop3, h, 110, MailSslMode.StartTls, prio++));
        }

        var tasks = candidates.Select(async c =>
        {
            var probe = await ProbePortAsync(c.Host, c.Port, c.Ssl == MailSslMode.ImplicitSsl, token);
            return (Candidate: c, Probe: probe);
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        var matched = results
            .Where(r => r.Probe.Reachable)
            .OrderBy(r => r.Candidate.Priority)
            .FirstOrDefault();

        if (matched.Probe != null && matched.Probe.Reachable)
        {
            var sslMode = matched.Candidate.Ssl;
            if (sslMode == MailSslMode.StartTls && !matched.Probe.SupportsStartTls)
                sslMode = MailSslMode.None;
            return (matched.Candidate.Protocol, matched.Candidate.Host, matched.Candidate.Port, sslMode);
        }

        return null;
    }

    private static async Task<(string Host, int Port, MailSslMode Ssl)?> ProbeSmtpEndpointsAsync(
        string[] hosts,
        CancellationToken token)
    {
        var candidates = new List<(string Host, int Port, MailSslMode Ssl, int Priority)>();
        int prio = 0;
        foreach (string h in hosts.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add((h, 465, MailSslMode.ImplicitSsl, prio++));
            candidates.Add((h, 587, MailSslMode.StartTls, prio++));
            candidates.Add((h, 25, MailSslMode.StartTls, prio++));
        }

        var tasks = candidates.Select(async c =>
        {
            var probe = await ProbePortAsync(c.Host, c.Port, c.Ssl == MailSslMode.ImplicitSsl, token);
            return (Candidate: c, Probe: probe);
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        var matched = results
            .Where(r => r.Probe.Reachable)
            .OrderBy(r => r.Candidate.Priority)
            .FirstOrDefault();

        if (matched.Probe != null && matched.Probe.Reachable)
        {
            var sslMode = matched.Candidate.Ssl;
            if (sslMode == MailSslMode.StartTls && !matched.Probe.SupportsStartTls)
                sslMode = MailSslMode.None;
            return (matched.Candidate.Host, matched.Candidate.Port, sslMode);
        }

        return null;
    }

    public sealed record PortProbeResult(bool Reachable, string Banner, bool SupportsStartTls);

    public static async Task<PortProbeResult> ProbePortAsync(string host, int port, bool useImplicitSsl, CancellationToken token = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(2200));

        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port, cts.Token);

            Stream stream = tcp.GetStream();
            if (useImplicitSsl)
            {
                var ssl = new SslStream(stream, false, (_, _, _, _) => true);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, cts.Token);
                stream = ssl;
            }

            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            await using var writer = new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };

            string? banner = await reader.ReadLineAsync(cts.Token);
            if (string.IsNullOrEmpty(banner)) return new PortProbeResult(false, "", false);

            bool supportsStartTls = banner.Contains("STARTTLS", StringComparison.OrdinalIgnoreCase);
            if (!useImplicitSsl)
            {
                if (banner.StartsWith("* OK", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("a001 CAPABILITY");
                    for (int i = 0; i < 6; i++)
                    {
                        string? line = await reader.ReadLineAsync(cts.Token);
                        if (line == null) break;
                        if (line.Contains("STARTTLS", StringComparison.OrdinalIgnoreCase)) supportsStartTls = true;
                        if (line.StartsWith("a001 ", StringComparison.OrdinalIgnoreCase)) break;
                    }
                }
                else if (banner.StartsWith("220", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("EHLO pixelhelper.local");
                    for (int i = 0; i < 12; i++)
                    {
                        string? line = await reader.ReadLineAsync(cts.Token);
                        if (line == null) break;
                        if (line.Contains("STARTTLS", StringComparison.OrdinalIgnoreCase)) supportsStartTls = true;
                        if (line.Length >= 4 && line[3] == ' ') break;
                    }
                }
                else if (banner.StartsWith("+OK", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("CAPA");
                    for (int i = 0; i < 10; i++)
                    {
                        string? line = await reader.ReadLineAsync(cts.Token);
                        if (line == null || line == ".") break;
                        if (line.Contains("STLS", StringComparison.OrdinalIgnoreCase)) supportsStartTls = true;
                    }
                }
            }

            return new PortProbeResult(true, banner, supportsStartTls);
        }
        catch
        {
            return new PortProbeResult(false, "", false);
        }
    }
}

