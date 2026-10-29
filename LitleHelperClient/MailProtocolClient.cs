using System.Globalization;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace PixelHelper;

public sealed record ParsedAttachmentData(string FileName, string ContentType, byte[] Content);

public sealed record ParsedMimeMessage(
    MailMessageItem Message,
    byte[] RawMimeBytes,
    List<ParsedAttachmentData> RawAttachments
);

public static class MailProtocolClient
{
    static MailProtocolClient()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static async Task<(bool Success, string Message)> TestConnectionAsync(
        MailAccountConfig account,
        string plainPassword,
        CancellationToken token = default)
    {
        try
        {
            string login = string.IsNullOrWhiteSpace(account.Username) ? account.EmailAddress : account.Username;
            if (account.IncomingProtocol == MailIncomingProtocol.Imap)
            {
                await VerifyImapLoginAsync(account.IncomingHost, account.IncomingPort, account.IncomingSslMode, login, plainPassword, token);
            }
            else
            {
                await VerifyPop3LoginAsync(account.IncomingHost, account.IncomingPort, account.IncomingSslMode, login, plainPassword, token);
            }

            if (!string.IsNullOrWhiteSpace(account.SmtpHost))
            {
                await VerifySmtpLoginAsync(account.SmtpHost, account.SmtpPort, account.SmtpSslMode, login, plainPassword, token);
            }

            return (true, $"Подключение успешно проверено ({account.IncomingProtocol.ToString().ToUpperInvariant()} {account.IncomingHost}:{account.IncomingPort}, SMTP {account.SmtpHost}:{account.SmtpPort}).");
        }
        catch (Exception ex)
        {
            return (false, "Ошибка подключения: " + ex.Message);
        }
    }

    public static async Task<FetchResult> FetchNewMessagesAsync(
        MailAccountConfig account,
        string plainPassword,
        HashSet<string> knownUids,
        int maxMessages = 40,
        CancellationToken token = default)
    {
        string login = string.IsNullOrWhiteSpace(account.Username) ? account.EmailAddress : account.Username;
        bool deleteFromServer = account.StorageMode == MailStorageMode.DownloadAllToComputer && account.DeleteFromServerAfterDownload;

        if (account.IncomingProtocol == MailIncomingProtocol.Imap)
        {
            return await FetchImapAsync(
                account.IncomingHost,
                account.IncomingPort,
                account.IncomingSslMode,
                login,
                plainPassword,
                knownUids,
                deleteFromServer,
                maxMessages,
                token);
        }

        var pop3List = await FetchPop3Async(
            account.IncomingHost,
            account.IncomingPort,
            account.IncomingSslMode,
            login,
            plainPassword,
            knownUids,
            deleteFromServer,
            maxMessages,
            token);
        return new FetchResult(pop3List, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    public static async Task<List<FetchedMessageItem>> FetchAndPurgeAllAsync(
        MailAccountConfig account,
        string plainPassword,
        CancellationToken token = default)
    {
        string login = string.IsNullOrWhiteSpace(account.Username) ? account.EmailAddress : account.Username;
        var emptyKnown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (account.IncomingProtocol == MailIncomingProtocol.Imap)
        {
            var imapResult = await FetchImapAsync(
                account.IncomingHost,
                account.IncomingPort,
                account.IncomingSslMode,
                login,
                plainPassword,
                emptyKnown,
                deleteFromServer: true,
                maxMessages: 10000,
                token: token);
            return imapResult.Messages;
        }

        return await FetchPop3Async(
            account.IncomingHost,
            account.IncomingPort,
            account.IncomingSslMode,
            login,
            plainPassword,
            emptyKnown,
            deleteFromServer: true,
            maxMessages: 10000,
            token: token);
    }

    private static async Task<(TcpClient Tcp, Stream Stream, StreamReader Reader, StreamWriter Writer)> ConnectAndUpgradeAsync(
        string host,
        int port,
        MailSslMode sslMode,
        string startTlsCommand,
        CancellationToken token)
    {
        var tcp = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await tcp.ConnectAsync(host, port, cts.Token);

        Stream netStream = tcp.GetStream();
        if (sslMode == MailSslMode.ImplicitSsl)
        {
            var ssl = new SslStream(netStream, false, (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, cts.Token);
            netStream = ssl;
        }

        var reader = new StreamReader(netStream, Encoding.UTF8, false, 4096, leaveOpen: true);
        var writer = new StreamWriter(netStream, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };

        string? banner = await reader.ReadLineAsync(cts.Token);
        if (string.IsNullOrEmpty(banner))
        {
            tcp.Dispose();
            throw new IOException($"Сервер {host}:{port} закрыл соединение без приветствия.");
        }

        if (sslMode == MailSslMode.StartTls && !string.IsNullOrEmpty(startTlsCommand))
        {
            if (startTlsCommand.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("EHLO pixelhelper.local");
                while (true)
                {
                    string? line = await reader.ReadLineAsync(cts.Token);
                    if (line == null || (line.Length >= 4 && line[3] == ' ')) break;
                }
                await writer.WriteLineAsync("STARTTLS");
                string? tlsResp = await reader.ReadLineAsync(cts.Token);
                if (tlsResp == null || !tlsResp.StartsWith("220"))
                    throw new IOException($"Сервер {host}:{port} отклонил STARTTLS: {tlsResp}");
            }
            else
            {
                await writer.WriteLineAsync(startTlsCommand);
                string? tlsResp = await reader.ReadLineAsync(cts.Token);
                if (tlsResp == null || (!tlsResp.Contains("OK", StringComparison.OrdinalIgnoreCase) && !tlsResp.StartsWith("+OK")))
                    throw new IOException($"Сервер {host}:{port} отклонил переход на TLS: {tlsResp}");
            }

            var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, cts.Token);
            reader = new StreamReader(ssl, Encoding.UTF8, false, 4096, leaveOpen: true);
            writer = new StreamWriter(ssl, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
            netStream = ssl;
        }

        return (tcp, netStream, reader, writer);
    }

    private static string QuoteImap(string value) =>
        "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static async Task VerifyImapLoginAsync(string host, int port, MailSslMode ssl, string login, string password, CancellationToken token)
    {
        var conn = await ConnectAndUpgradeAsync(host, port, ssl, "a000 STARTTLS", token);
        using (conn.Tcp)
        await using (conn.Stream)
        {
            await conn.Writer.WriteLineAsync($"a001 LOGIN {QuoteImap(login)} {QuoteImap(password)}");
            while (true)
            {
                string? line = await conn.Reader.ReadLineAsync(token);
                if (line == null) throw new IOException("Обрыв соединения при авторизации IMAP.");
                if (line.StartsWith("a001 ", StringComparison.OrdinalIgnoreCase))
                {
                    if (!line.StartsWith("a001 OK", StringComparison.OrdinalIgnoreCase))
                        throw new UnauthorizedAccessException("Ошибка авторизации IMAP: " + line);
                    break;
                }
            }
            await conn.Writer.WriteLineAsync("a002 LOGOUT");
        }
    }

    private static async Task VerifyPop3LoginAsync(string host, int port, MailSslMode ssl, string login, string password, CancellationToken token)
    {
        var conn = await ConnectAndUpgradeAsync(host, port, ssl, "STLS", token);
        using (conn.Tcp)
        await using (conn.Stream)
        {
            await conn.Writer.WriteLineAsync($"USER {login}");
            string? userResp = await conn.Reader.ReadLineAsync(token);
            if (userResp == null || !userResp.StartsWith("+OK", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Ошибка USER POP3: " + userResp);

            await conn.Writer.WriteLineAsync($"PASS {password}");
            string? passResp = await conn.Reader.ReadLineAsync(token);
            if (passResp == null || !passResp.StartsWith("+OK", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Ошибка авторизации POP3: " + passResp);

            await conn.Writer.WriteLineAsync("QUIT");
        }
    }

    private static async Task VerifySmtpLoginAsync(string host, int port, MailSslMode ssl, string login, string password, CancellationToken token)
    {
        var conn = await ConnectAndUpgradeAsync(host, port, ssl, "EHLO", token);
        using (conn.Tcp)
        await using (conn.Stream)
        {
            await conn.Writer.WriteLineAsync("EHLO pixelhelper.local");
            bool supportsAuth = false;
            while (true)
            {
                string? line = await conn.Reader.ReadLineAsync(token);
                if (line == null) break;
                if (line.Contains("AUTH", StringComparison.OrdinalIgnoreCase)) supportsAuth = true;
                if (line.Length >= 4 && line[3] == ' ') break;
            }

            if (supportsAuth && !string.IsNullOrEmpty(password))
            {
                string authPlain = Convert.ToBase64String(Encoding.UTF8.GetBytes($"\0{login}\0{password}"));
                await conn.Writer.WriteLineAsync($"AUTH PLAIN {authPlain}");
                string? authResp = await conn.Reader.ReadLineAsync(token);
                if (authResp != null && !authResp.StartsWith("235"))
                {
                    await conn.Writer.WriteLineAsync("AUTH LOGIN");
                    string? step1 = await conn.Reader.ReadLineAsync(token);
                    if (step1 != null && step1.StartsWith("334"))
                    {
                        await conn.Writer.WriteLineAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(login)));
                        await conn.Reader.ReadLineAsync(token);
                        await conn.Writer.WriteLineAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(password)));
                        authResp = await conn.Reader.ReadLineAsync(token);
                    }
                    if (authResp == null || !authResp.StartsWith("235"))
                        throw new UnauthorizedAccessException("Ошибка авторизации SMTP: " + authResp);
                }
            }

            await conn.Writer.WriteLineAsync("QUIT");
        }
    }

    private static async Task<FetchResult> FetchImapAsync(
        string host,
        int port,
        MailSslMode ssl,
        string login,
        string password,
        HashSet<string> knownUids,
        bool deleteFromServer,
        int maxMessages,
        CancellationToken token)
    {
        var result = new List<FetchedMessageItem>();
        var seenUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conn = await ConnectAndUpgradeAsync(host, port, ssl, "a000 STARTTLS", token);
        using (conn.Tcp)
        await using (conn.Stream)
        {
            await conn.Writer.WriteLineAsync($"a001 LOGIN {QuoteImap(login)} {QuoteImap(password)}");
            while (true)
            {
                string? line = await conn.Reader.ReadLineAsync(token);
                if (line == null) throw new IOException("Обрыв соединения IMAP.");
                if (line.StartsWith("a001 ", StringComparison.OrdinalIgnoreCase))
                {
                    if (!line.StartsWith("a001 OK", StringComparison.OrdinalIgnoreCase))
                        throw new UnauthorizedAccessException("Ошибка входа IMAP: " + line);
                    break;
                }
            }

            // 1. Fetch from INBOX
            await FetchFolderMessagesAsync(conn, "INBOX", MailFolderKind.Inbox, result, seenUids, knownUids, deleteFromServer, maxMessages, token);

            // 2. Discover Sent folder via LIST
            await conn.Writer.WriteLineAsync("a004 LIST \"\" \"*\"");
            string? sentFolder = null;
            while (true)
            {
                string? line = await conn.Reader.ReadLineAsync(token);
                if (line == null) break;
                if (line.StartsWith("* LIST", StringComparison.OrdinalIgnoreCase))
                {
                    string lower = line.ToLowerInvariant();
                    if (lower.Contains("\\sent") || lower.Contains("sent") || lower.Contains("отправленн"))
                    {
                        var match = Regex.Match(line, "\"[^\"]+\"\\s+\"?([^\"]+)\"?$|(\"[^\"]+\")$");
                        if (match.Success)
                        {
                            string candidate = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value.Trim('"');
                            if (!candidate.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
                            {
                                sentFolder = candidate;
                            }
                        }
                    }
                }
                if (line.StartsWith("a004 ", StringComparison.OrdinalIgnoreCase)) break;
            }

            // 3. Fetch from Sent folder if discovered
            if (!string.IsNullOrWhiteSpace(sentFolder))
            {
                await FetchFolderMessagesAsync(conn, sentFolder, MailFolderKind.Sent, result, seenUids, knownUids, deleteFromServer, maxMessages, token);
            }

            await conn.Writer.WriteLineAsync("a999 LOGOUT");
        }

        return new FetchResult(result, seenUids);
    }

    private static async Task FetchFolderMessagesAsync(
        (TcpClient Tcp, Stream Stream, StreamReader Reader, StreamWriter Writer) conn,
        string folderName,
        MailFolderKind folderKind,
        List<FetchedMessageItem> result,
        HashSet<string> seenUids,
        HashSet<string> knownUids,
        bool deleteFromServer,
        int maxMessages,
        CancellationToken token)
    {
        string selTag = "s" + Guid.NewGuid().ToString("N")[..4];
        await conn.Writer.WriteLineAsync($"{selTag} SELECT {QuoteImap(folderName)}");
        bool selectOk = false;
        while (true)
        {
            string? line = await conn.Reader.ReadLineAsync(token);
            if (line == null) break;
            if (line.StartsWith(selTag + " ", StringComparison.OrdinalIgnoreCase))
            {
                selectOk = line.StartsWith(selTag + " OK", StringComparison.OrdinalIgnoreCase);
                break;
            }
        }
        if (!selectOk) return;

        string prefix = folderKind == MailFolderKind.Sent ? "sent_" : "";

        // Query UID SEARCH SEEN to detect all read messages
        string seenTag = "q" + Guid.NewGuid().ToString("N")[..4];
        await conn.Writer.WriteLineAsync($"{seenTag} UID SEARCH SEEN");
        var folderSeenUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            string? line = await conn.Reader.ReadLineAsync(token);
            if (line == null) break;
            if (line.StartsWith("* SEARCH", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Substring(8).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var p in parts)
                {
                    folderSeenUids.Add(p);
                    seenUids.Add(prefix + p);
                }
            }
            if (line.StartsWith(seenTag + " ", StringComparison.OrdinalIgnoreCase)) break;
        }

        string searchTag = "a" + Guid.NewGuid().ToString("N")[..4];
        await conn.Writer.WriteLineAsync($"{searchTag} UID SEARCH ALL");
        var uids = new List<string>();
        while (true)
        {
            string? line = await conn.Reader.ReadLineAsync(token);
            if (line == null) break;
            if (line.StartsWith("* SEARCH", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Substring(8).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                uids.AddRange(parts);
            }
            if (line.StartsWith(searchTag + " ", StringComparison.OrdinalIgnoreCase)) break;
        }

        var toFetch = uids.Where(u => !knownUids.Contains(prefix + u)).TakeLast(maxMessages).ToList();
        int cmdIdx = 10;
        foreach (string uid in toFetch)
        {
            string tag = $"f{cmdIdx++:D3}";
            await conn.Writer.WriteLineAsync($"{tag} UID FETCH {uid} (FLAGS BODY.PEEK[])");
            var rawBuilder = new StringBuilder();
            bool inBody = false;
            bool isSeen = folderKind == MailFolderKind.Sent || folderSeenUids.Contains(uid);

            while (true)
            {
                string? line = await conn.Reader.ReadLineAsync(token);
                if (line == null) break;
                if (line.StartsWith(tag + " ", StringComparison.OrdinalIgnoreCase)) break;
                if (!inBody && line.StartsWith("* ", StringComparison.OrdinalIgnoreCase) && line.Contains("FETCH", StringComparison.OrdinalIgnoreCase))
                {
                    if (line.Contains("\\Seen", StringComparison.OrdinalIgnoreCase))
                    {
                        isSeen = true;
                    }
                    inBody = true;
                    continue;
                }
                if (inBody)
                {
                    rawBuilder.AppendLine(line);
                }
            }

            string rawText = rawBuilder.ToString().TrimEnd();
            rawText = Regex.Replace(rawText, @"(\r?\n)?\s*(?:FLAGS\s*\([^\)]*\)\s*)?(?:UID\s+\d+\s*)?\)\s*$", "", RegexOptions.IgnoreCase).TrimEnd();

            if (!string.IsNullOrWhiteSpace(rawText))
            {
                result.Add(new FetchedMessageItem(prefix + uid, Encoding.UTF8.GetBytes(rawText), folderKind, isSeen));

                if (deleteFromServer)
                {
                    string delTag = $"d{cmdIdx++:D3}";
                    await conn.Writer.WriteLineAsync($"{delTag} UID STORE {uid} +FLAGS (\\Deleted)");
                    while (true)
                    {
                        string? l = await conn.Reader.ReadLineAsync(token);
                        if (l == null || l.StartsWith(delTag + " ", StringComparison.OrdinalIgnoreCase)) break;
                    }
                }
            }
        }

        if (deleteFromServer && toFetch.Count > 0)
        {
            string expTag = "x" + Guid.NewGuid().ToString("N")[..4];
            await conn.Writer.WriteLineAsync($"{expTag} EXPUNGE");
            while (true)
            {
                string? l = await conn.Reader.ReadLineAsync(token);
                if (l == null || l.StartsWith(expTag + " ", StringComparison.OrdinalIgnoreCase)) break;
            }
        }
    }

    public static async Task<bool> WaitImapIdleAsync(
        MailAccountConfig account,
        string plainPassword,
        TimeSpan maxWait,
        CancellationToken token)
    {
        if (account.IncomingProtocol != MailIncomingProtocol.Imap || string.IsNullOrWhiteSpace(account.IncomingHost))
        {
            await Task.Delay(TimeSpan.FromSeconds(15), token);
            return false;
        }

        try
        {
            string login = string.IsNullOrWhiteSpace(account.Username) ? account.EmailAddress : account.Username;
            var conn = await ConnectAndUpgradeAsync(account.IncomingHost, account.IncomingPort, account.IncomingSslMode, "a000 STARTTLS", token);
            using (conn.Tcp)
            await using (conn.Stream)
            {
                await conn.Writer.WriteLineAsync($"a001 LOGIN {QuoteImap(login)} {QuoteImap(plainPassword)}");
                while (true)
                {
                    string? line = await conn.Reader.ReadLineAsync(token);
                    if (line == null) return false;
                    if (line.StartsWith("a001 ", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!line.StartsWith("a001 OK", StringComparison.OrdinalIgnoreCase))
                            return false;
                        break;
                    }
                }

                await conn.Writer.WriteLineAsync("a002 SELECT INBOX");
                while (true)
                {
                    string? line = await conn.Reader.ReadLineAsync(token);
                    if (line == null || line.StartsWith("a002 ", StringComparison.OrdinalIgnoreCase)) break;
                }

                await conn.Writer.WriteLineAsync("a003 IDLE");
                string? idleStart = await conn.Reader.ReadLineAsync(token);
                if (idleStart == null || !idleStart.StartsWith("+"))
                {
                    // Server does not support IDLE — wait 15 seconds and return
                    await conn.Writer.WriteLineAsync("a004 LOGOUT");
                    await Task.Delay(TimeSpan.FromSeconds(15), token);
                    return false;
                }

                bool eventReceived = false;
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(maxWait);

                try
                {
                    while (!timeoutCts.IsCancellationRequested)
                    {
                        string? pushLine = await conn.Reader.ReadLineAsync(timeoutCts.Token);
                        if (pushLine == null) break;
                        if (pushLine.StartsWith("* ", StringComparison.OrdinalIgnoreCase) &&
                            (pushLine.Contains("EXISTS", StringComparison.OrdinalIgnoreCase) ||
                             pushLine.Contains("RECENT", StringComparison.OrdinalIgnoreCase) ||
                             pushLine.Contains("FETCH", StringComparison.OrdinalIgnoreCase)))
                        {
                            eventReceived = true;
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    // Normal periodic IDLE refresh timeout
                }

                try
                {
                    await conn.Writer.WriteLineAsync("DONE");
                    await conn.Writer.WriteLineAsync("a004 LOGOUT");
                }
                catch { }

                return eventReceived;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await Task.Delay(TimeSpan.FromSeconds(12), token);
            return false;
        }
    }

    private static async Task<List<FetchedMessageItem>> FetchPop3Async(
        string host,
        int port,
        MailSslMode ssl,
        string login,
        string password,
        HashSet<string> knownUids,
        bool deleteFromServer,
        int maxMessages,
        CancellationToken token)
    {
        var result = new List<FetchedMessageItem>();
        var conn = await ConnectAndUpgradeAsync(host, port, ssl, "STLS", token);
        using (conn.Tcp)
        await using (conn.Stream)
        {
            await conn.Writer.WriteLineAsync($"USER {login}");
            string? uResp = await conn.Reader.ReadLineAsync(token);
            if (uResp == null || !uResp.StartsWith("+OK", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Ошибка USER POP3: " + uResp);

            await conn.Writer.WriteLineAsync($"PASS {password}");
            string? pResp = await conn.Reader.ReadLineAsync(token);
            if (pResp == null || !pResp.StartsWith("+OK", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Ошибка PASS POP3: " + pResp);

            await conn.Writer.WriteLineAsync("UIDL");
            string? uidlHeader = await conn.Reader.ReadLineAsync(token);
            var msgList = new List<(int MsgNum, string Uid)>();

            if (uidlHeader != null && uidlHeader.StartsWith("+OK", StringComparison.OrdinalIgnoreCase))
            {
                while (true)
                {
                    string? line = await conn.Reader.ReadLineAsync(token);
                    if (line == null || line == ".") break;
                    var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (parts.Length == 2 && int.TryParse(parts[0], out int num))
                    {
                        msgList.Add((num, parts[1]));
                    }
                }
            }

            var toFetch = msgList.Where(m => !knownUids.Contains(m.Uid)).TakeLast(maxMessages).ToList();
            foreach (var item in toFetch)
            {
                await conn.Writer.WriteLineAsync($"RETR {item.MsgNum}");
                string? retrResp = await conn.Reader.ReadLineAsync(token);
                if (retrResp == null || !retrResp.StartsWith("+OK", StringComparison.OrdinalIgnoreCase))
                    continue;

                var sb = new StringBuilder();
                while (true)
                {
                    string? line = await conn.Reader.ReadLineAsync(token);
                    if (line == null || line == ".") break;
                    if (line.StartsWith("..")) line = line[1..];
                    sb.AppendLine(line);
                }

                result.Add(new FetchedMessageItem(item.Uid, Encoding.UTF8.GetBytes(sb.ToString()), MailFolderKind.Inbox, false));

                if (deleteFromServer)
                {
                    await conn.Writer.WriteLineAsync($"DELE {item.MsgNum}");
                    await conn.Reader.ReadLineAsync(token);
                }
            }

            await conn.Writer.WriteLineAsync("QUIT");
        }

        return result;
    }

    public static async Task SetImapSeenFlagAsync(
        MailAccountConfig account,
        string plainPassword,
        string remoteUid,
        MailFolderKind folder,
        bool isRead,
        CancellationToken token = default)
    {
        if (account.IncomingProtocol != MailIncomingProtocol.Imap || string.IsNullOrWhiteSpace(account.IncomingHost))
            return;

        string login = string.IsNullOrWhiteSpace(account.Username) ? account.EmailAddress : account.Username;
        string cleanUid = remoteUid;
        if (cleanUid.StartsWith("sent_", StringComparison.OrdinalIgnoreCase))
        {
            cleanUid = cleanUid.Substring(5);
        }

        try
        {
            var conn = await ConnectAndUpgradeAsync(account.IncomingHost, account.IncomingPort, account.IncomingSslMode, "a000 STARTTLS", token);
            using (conn.Tcp)
            await using (conn.Stream)
            {
                await conn.Writer.WriteLineAsync($"a001 LOGIN {QuoteImap(login)} {QuoteImap(plainPassword)}");
                while (true)
                {
                    string? line = await conn.Reader.ReadLineAsync(token);
                    if (line == null) return;
                    if (line.StartsWith("a001 ", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!line.StartsWith("a001 OK", StringComparison.OrdinalIgnoreCase)) return;
                        break;
                    }
                }

                string folderName = "INBOX";
                if (folder == MailFolderKind.Sent)
                {
                    await conn.Writer.WriteLineAsync("a002 LIST \"\" \"*\"");
                    while (true)
                    {
                        string? line = await conn.Reader.ReadLineAsync(token);
                        if (line == null) break;
                        if (line.StartsWith("* LIST", StringComparison.OrdinalIgnoreCase))
                        {
                            string lower = line.ToLowerInvariant();
                            if (lower.Contains("\\sent") || lower.Contains("sent") || lower.Contains("отправленн"))
                            {
                                var match = Regex.Match(line, "\"[^\"]+\"\\s+\"?([^\"]+)\"?$|(\"[^\"]+\")$");
                                if (match.Success)
                                {
                                    string candidate = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value.Trim('"');
                                    if (!candidate.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
                                    {
                                        folderName = candidate;
                                    }
                                }
                            }
                        }
                        if (line.StartsWith("a002 ", StringComparison.OrdinalIgnoreCase)) break;
                    }
                }

                await conn.Writer.WriteLineAsync($"a003 SELECT {QuoteImap(folderName)}");
                while (true)
                {
                    string? line = await conn.Reader.ReadLineAsync(token);
                    if (line == null || line.StartsWith("a003 ", StringComparison.OrdinalIgnoreCase)) break;
                }

                string flagCmd = isRead ? "+FLAGS (\\Seen)" : "-FLAGS (\\Seen)";
                await conn.Writer.WriteLineAsync($"a004 UID STORE {cleanUid} {flagCmd}");
                while (true)
                {
                    string? line = await conn.Reader.ReadLineAsync(token);
                    if (line == null || line.StartsWith("a004 ", StringComparison.OrdinalIgnoreCase)) break;
                }

                await conn.Writer.WriteLineAsync("a005 LOGOUT");
            }
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
        }
    }

    public static async Task SendSmtpAsync(
        MailAccountConfig account,
        string plainPassword,
        MailMessageItem outgoingMessage,
        List<ParsedAttachmentData>? attachments,
        string? encryptionPassphrase,
        CancellationToken token = default)
    {
        byte[] rawMime = BuildRawMime(account, outgoingMessage, attachments, encryptionPassphrase);
        string login = string.IsNullOrWhiteSpace(account.Username) ? account.EmailAddress : account.Username;

        var conn = await ConnectAndUpgradeAsync(account.SmtpHost, account.SmtpPort, account.SmtpSslMode, "EHLO", token);
        using (conn.Tcp)
        await using (conn.Stream)
        {
            await conn.Writer.WriteLineAsync("EHLO pixelhelper.local");
            bool supportsAuth = false;
            while (true)
            {
                string? line = await conn.Reader.ReadLineAsync(token);
                if (line == null) break;
                if (line.Contains("AUTH", StringComparison.OrdinalIgnoreCase)) supportsAuth = true;
                if (line.Length >= 4 && line[3] == ' ') break;
            }

            if (supportsAuth && !string.IsNullOrEmpty(plainPassword))
            {
                string authPlain = Convert.ToBase64String(Encoding.UTF8.GetBytes($"\0{login}\0{plainPassword}"));
                await conn.Writer.WriteLineAsync($"AUTH PLAIN {authPlain}");
                string? authResp = await conn.Reader.ReadLineAsync(token);
                if (authResp != null && !authResp.StartsWith("235"))
                {
                    await conn.Writer.WriteLineAsync("AUTH LOGIN");
                    string? step1 = await conn.Reader.ReadLineAsync(token);
                    if (step1 != null && step1.StartsWith("334"))
                    {
                        await conn.Writer.WriteLineAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(login)));
                        await conn.Reader.ReadLineAsync(token);
                        await conn.Writer.WriteLineAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(plainPassword)));
                        authResp = await conn.Reader.ReadLineAsync(token);
                    }
                    if (authResp == null || !authResp.StartsWith("235"))
                        throw new UnauthorizedAccessException("Ошибка авторизации SMTP: " + authResp);
                }
            }

            await conn.Writer.WriteLineAsync($"MAIL FROM:<{account.EmailAddress}>");
            string? mailFromResp = await conn.Reader.ReadLineAsync(token);
            if (mailFromResp == null || !mailFromResp.StartsWith("250"))
                throw new IOException("Сервер отклонил отправителя MAIL FROM: " + mailFromResp);

            var allRecipients = outgoingMessage.ToAddresses
                .Concat(outgoingMessage.CcAddresses)
                .Select(ExtractEmailAddress)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (string rcpt in allRecipients)
            {
                await conn.Writer.WriteLineAsync($"RCPT TO:<{rcpt}>");
                string? rcptResp = await conn.Reader.ReadLineAsync(token);
                if (rcptResp == null || (!rcptResp.StartsWith("250") && !rcptResp.StartsWith("251")))
                    throw new IOException($"Сервер отклонил получателя {rcpt}: {rcptResp}");
            }

            await conn.Writer.WriteLineAsync("DATA");
            string? dataResp = await conn.Reader.ReadLineAsync(token);
            if (dataResp == null || !dataResp.StartsWith("354"))
                throw new IOException("Сервер отклонил команду DATA: " + dataResp);

            string mimeString = Encoding.UTF8.GetString(rawMime);
            using (var sr = new StringReader(mimeString))
            {
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith(".")) line = "." + line;
                    await conn.Writer.WriteLineAsync(line);
                }
            }

            await conn.Writer.WriteLineAsync(".");
            string? endDataResp = await conn.Reader.ReadLineAsync(token);
            if (endDataResp == null || !endDataResp.StartsWith("250"))
                throw new IOException("Ошибка завершения отправки письма: " + endDataResp);

            await conn.Writer.WriteLineAsync("QUIT");
        }
    }

    public static byte[] BuildRawMime(
        MailAccountConfig account,
        MailMessageItem msg,
        List<ParsedAttachmentData>? attachments,
        string? encryptionPassphrase)
    {
        var sb = new StringBuilder();
        string fromHeader = string.IsNullOrWhiteSpace(account.DisplayName)
            ? $"<{account.EmailAddress}>"
            : $"{EncodeHeaderUtf8(account.DisplayName)} <{account.EmailAddress}>";

        sb.AppendLine($"From: {fromHeader}");
        sb.AppendLine($"To: {string.Join(", ", msg.ToAddresses)}");
        if (msg.CcAddresses.Count > 0)
            sb.AppendLine($"Cc: {string.Join(", ", msg.CcAddresses)}");
        sb.AppendLine($"Subject: {EncodeHeaderUtf8(msg.Subject)}");
        sb.AppendLine($"Date: {DateTimeOffset.UtcNow:R}");
        sb.AppendLine($"Message-ID: <{Guid.NewGuid():N}@pixelhelper.local>");
        sb.AppendLine("MIME-Version: 1.0");
        sb.AppendLine("X-Mailer: PixelHelper Mail Client");

        string bodyToSend = msg.BodyText ?? "";
        if (msg.IsEncrypted || msg.EncryptionType == MailEncryptionMode.PixelAes256)
        {
            sb.AppendLine("X-PixelHelper-Encryption: Pixel-AES-256");
            bodyToSend = MailCryptoService.EncryptMessagePixelAes(bodyToSend, encryptionPassphrase ?? "");
        }

        if (attachments == null || attachments.Count == 0)
        {
            sb.AppendLine("Content-Type: text/plain; charset=\"utf-8\"");
            sb.AppendLine("Content-Transfer-Encoding: base64");
            sb.AppendLine();
            sb.AppendLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(bodyToSend), Base64FormattingOptions.InsertLineBreaks));
        }
        else
        {
            string boundary = "----=_Part_PixelMail_" + Guid.NewGuid().ToString("N");
            sb.AppendLine($"Content-Type: multipart/mixed; boundary=\"{boundary}\"");
            sb.AppendLine();
            sb.AppendLine($"--{boundary}");
            sb.AppendLine("Content-Type: text/plain; charset=\"utf-8\"");
            sb.AppendLine("Content-Transfer-Encoding: base64");
            sb.AppendLine();
            sb.AppendLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(bodyToSend), Base64FormattingOptions.InsertLineBreaks));

            foreach (var att in attachments)
            {
                sb.AppendLine($"--{boundary}");
                string encodedName = EncodeHeaderUtf8(att.FileName);
                sb.AppendLine($"Content-Type: {att.ContentType}; name=\"{encodedName}\"");
                sb.AppendLine("Content-Transfer-Encoding: base64");
                sb.AppendLine($"Content-Disposition: attachment; filename=\"{encodedName}\"");
                sb.AppendLine();
                sb.AppendLine(Convert.ToBase64String(att.Content, Base64FormattingOptions.InsertLineBreaks));
            }
            sb.AppendLine($"--{boundary}--");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string EncodeHeaderUtf8(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (text.All(c => c >= 32 && c <= 126)) return text;
        return "=?utf-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) + "?=";
    }

    public static ParsedMimeMessage ParseMimeMessage(
        byte[] rawMimeBytes,
        string accountId,
        string accountEmail,
        string remoteUid,
        string? decryptionPassphrase = null)
    {
        string rawText = Encoding.UTF8.GetString(rawMimeBytes);
        var (headersRaw, bodyRaw) = SplitHeadersAndBody(rawText);
        var headers = ParseHeaders(headersRaw);

        string fromRaw = headers.GetValueOrDefault("from", "");
        var (fromName, fromAddress) = ParseAddress(fromRaw);
        string replyToRaw = headers.GetValueOrDefault("reply-to", "");
        var (_, replyToAddr) = ParseAddress(replyToRaw);

        string subject = DecodeHeaderWords(headers.GetValueOrDefault("subject", "(Без темы)"));
        if (string.IsNullOrWhiteSpace(subject)) subject = "(Без темы)";

        var toList = ParseAddressList(headers.GetValueOrDefault("to", ""));
        var ccList = ParseAddressList(headers.GetValueOrDefault("cc", ""));

        DateTime dateUtc = DateTime.UtcNow;
        if (headers.TryGetValue("date", out var dateStr))
        {
            string cleanedDate = Regex.Replace(dateStr, @"\s*\([^)]*\)\s*$", "").Trim();
            if (DateTimeOffset.TryParse(cleanedDate, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dto))
            {
                dateUtc = dto.UtcDateTime;
            }
        }

        var attachments = new List<ParsedAttachmentData>();
        var plainBuilder = new StringBuilder();
        var htmlBuilder = new StringBuilder();

        ParseMimeEntity(headers, bodyRaw, plainBuilder, htmlBuilder, attachments);

        string bodyText = plainBuilder.ToString().Trim();
        string bodyHtml = htmlBuilder.ToString().Trim();

        if (string.IsNullOrWhiteSpace(bodyText) && !string.IsNullOrWhiteSpace(bodyHtml))
        {
            bodyText = StripHtmlTags(bodyHtml);
        }

        bool isEncrypted = headers.TryGetValue("x-pixelhelper-encryption", out var encHeader) &&
                           encHeader.Contains("Pixel-AES", StringComparison.OrdinalIgnoreCase);
        var encMode = MailEncryptionMode.None;

        if (isEncrypted || MailCryptoService.IsPixelAesEncrypted(bodyText))
        {
            isEncrypted = true;
            encMode = MailEncryptionMode.PixelAes256;
            if (MailCryptoService.TryDecryptMessagePixelAes(bodyText, decryptionPassphrase ?? "", out string decrypted))
            {
                bodyText = decrypted;
            }
        }
        else if (headers.TryGetValue("content-type", out var ct) &&
                 (ct.Contains("pkcs7", StringComparison.OrdinalIgnoreCase) || ct.Contains("x-pkcs7", StringComparison.OrdinalIgnoreCase)))
        {
            isEncrypted = true;
            encMode = MailEncryptionMode.SMime;
        }

        bool sigVerified = headers.TryGetValue("content-type", out var ctSig) &&
                           ctSig.Contains("multipart/signed", StringComparison.OrdinalIgnoreCase);

        string snippet = Regex.Replace(bodyText, @"\s+", " ").Trim();
        if (snippet.Length > 140) snippet = snippet[..140] + "…";

        var item = new MailMessageItem
        {
            AccountId = accountId,
            AccountEmail = accountEmail,
            RemoteUid = remoteUid,
            Folder = MailFolderKind.Inbox,
            Subject = subject,
            FromName = fromName,
            FromAddress = fromAddress,
            ReplyToAddress = replyToAddr,
            ToAddresses = toList,
            CcAddresses = ccList,
            DateUtc = dateUtc,
            Snippet = snippet,
            BodyText = bodyText,
            BodyHtml = bodyHtml,
            HeadersRaw = headersRaw,
            IsEncrypted = isEncrypted,
            EncryptionType = encMode,
            SignatureVerified = sigVerified
        };

        return new ParsedMimeMessage(item, rawMimeBytes, attachments);
    }

    private static void ParseMimeEntity(
        Dictionary<string, string> headers,
        string bodyRaw,
        StringBuilder plainBuilder,
        StringBuilder htmlBuilder,
        List<ParsedAttachmentData> attachments)
    {
        string contentType = headers.GetValueOrDefault("content-type", "text/plain; charset=utf-8");
        string transferEncoding = headers.GetValueOrDefault("content-transfer-encoding", "7bit").Trim().ToLowerInvariant();
        string contentDisposition = headers.GetValueOrDefault("content-disposition", "");

        if (contentType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
        {
            string? boundary = ExtractParameter(contentType, "boundary");
            if (!string.IsNullOrEmpty(boundary))
            {
                string delim = "--" + boundary;
                var parts = bodyRaw.Split(new[] { delim }, StringSplitOptions.None);
                // First part is preamble, last part starting with "--" is epilogue
                for (int i = 1; i < parts.Length; i++)
                {
                    string part = parts[i];
                    if (part.StartsWith("--")) break;
                    part = part.TrimStart('\r', '\n');
                    var (partHeadersRaw, partBodyRaw) = SplitHeadersAndBody(part);
                    var partHeaders = ParseHeaders(partHeadersRaw);
                    ParseMimeEntity(partHeaders, partBodyRaw, plainBuilder, htmlBuilder, attachments);
                }
                return;
            }
        }

        string? fileName = ExtractParameter(contentDisposition, "filename") ?? ExtractParameter(contentType, "name");
        bool isAttachment = contentDisposition.StartsWith("attachment", StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrWhiteSpace(fileName) && !contentType.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase) && !contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase));

        byte[] decodedBytes = DecodeTransferBytes(bodyRaw, transferEncoding, ExtractParameter(contentType, "charset"));

        if (isAttachment || (!contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(fileName)))
        {
            string cleanName = DecodeHeaderWords(fileName ?? "attachment.bin");
            cleanName = Path.GetFileName(cleanName);
            if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "attachment.bin";
            string mimeType = contentType.Split(';')[0].Trim();
            attachments.Add(new ParsedAttachmentData(cleanName, mimeType, decodedBytes));
            return;
        }

        Encoding charset = GetCharsetEncoding(ExtractParameter(contentType, "charset"));
        string textContent = charset.GetString(decodedBytes);

        if (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            if (htmlBuilder.Length > 0) htmlBuilder.AppendLine();
            htmlBuilder.Append(textContent);
        }
        else
        {
            if (plainBuilder.Length > 0) plainBuilder.AppendLine();
            plainBuilder.Append(textContent);
        }
    }

    private static byte[] DecodeTransferBytes(string bodyRaw, string transferEncoding, string? charsetName)
    {
        if (transferEncoding == "base64")
        {
            try
            {
                string cleaned = Regex.Replace(bodyRaw, @"\s+", "");
                return Convert.FromBase64String(cleaned);
            }
            catch
            {
                return Encoding.UTF8.GetBytes(bodyRaw);
            }
        }

        if (transferEncoding == "quoted-printable")
        {
            return DecodeQuotedPrintableBytes(bodyRaw);
        }

        Encoding enc = GetCharsetEncoding(charsetName);
        return enc.GetBytes(bodyRaw);
    }

    private static byte[] DecodeQuotedPrintableBytes(string input)
    {
        using var ms = new MemoryStream();
        // Remove soft line breaks "=\r\n" or "=\n"
        string softRemoved = Regex.Replace(input, @"=\r?\n", "");
        for (int i = 0; i < softRemoved.Length; i++)
        {
            char c = softRemoved[i];
            if (c == '=' && i + 2 < softRemoved.Length &&
                byte.TryParse(softRemoved.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            {
                ms.WriteByte(b);
                i += 2;
            }
            else
            {
                ms.WriteByte((byte)c);
            }
        }
        return ms.ToArray();
    }

    private static Encoding GetCharsetEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset)) return Encoding.UTF8;
        try
        {
            return Encoding.GetEncoding(charset.Trim().Trim('"'));
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private static (string HeadersRaw, string BodyRaw) SplitHeadersAndBody(string raw)
    {
        int idx = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (idx >= 0)
        {
            return (raw[..idx], raw[(idx + 4)..]);
        }
        idx = raw.IndexOf("\n\n", StringComparison.Ordinal);
        if (idx >= 0)
        {
            return (raw[..idx], raw[(idx + 2)..]);
        }
        return (raw, "");
    }

    private static Dictionary<string, string> ParseHeaders(string headersRaw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(headersRaw);
        string? currentName = null;
        var currentVal = new StringBuilder();

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                if (currentName != null)
                {
                    currentVal.Append(' ').Append(line.Trim());
                }
                continue;
            }

            if (currentName != null)
            {
                if (result.TryGetValue(currentName, out var prev))
                    result[currentName] = prev + "; " + currentVal.ToString().Trim();
                else
                    result[currentName] = currentVal.ToString().Trim();
            }

            int colon = line.IndexOf(':');
            if (colon > 0)
            {
                currentName = line[..colon].Trim();
                currentVal.Clear();
                currentVal.Append(line[(colon + 1)..].Trim());
            }
            else
            {
                currentName = null;
                currentVal.Clear();
            }
        }

        if (currentName != null)
        {
            if (result.TryGetValue(currentName, out var prev))
                result[currentName] = prev + "; " + currentVal.ToString().Trim();
            else
                result[currentName] = currentVal.ToString().Trim();
        }

        return result;
    }

    private static string? ExtractParameter(string headerValue, string paramName)
    {
        if (string.IsNullOrWhiteSpace(headerValue)) return null;
        var match = Regex.Match(headerValue, paramName + @"\*?\s*=\s*(?:""([^""]+)""|([^;\s]+))", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        string val = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        if (val.StartsWith("UTF-8''", StringComparison.OrdinalIgnoreCase))
        {
            val = Uri.UnescapeDataString(val[7..]);
        }
        return val;
    }

    public static string DecodeHeaderWords(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        // Remove whitespace between adjacent encoded words
        string normalized = Regex.Replace(input, @"\?=\s+=\?", "?==?");
        return Regex.Replace(normalized, @"=\?([^?]+)\?([BbQq])\?([^?]*)\?=", match =>
        {
            string charset = match.Groups[1].Value;
            string encoding = match.Groups[2].Value.ToUpperInvariant();
            string data = match.Groups[3].Value;
            Encoding enc = GetCharsetEncoding(charset);
            try
            {
                if (encoding == "B")
                {
                    return enc.GetString(Convert.FromBase64String(data));
                }
                else
                {
                    string qCleaned = data.Replace('_', ' ');
                    byte[] bytes = DecodeQuotedPrintableBytes(qCleaned);
                    return enc.GetString(bytes);
                }
            }
            catch
            {
                return match.Value;
            }
        });
    }

    public static (string Name, string Address) ParseAddress(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ("", "");
        string decoded = DecodeHeaderWords(raw).Trim();
        var angleMatch = Regex.Match(decoded, @"^(.*?)<([^>]+)>");
        if (angleMatch.Success)
        {
            string name = angleMatch.Groups[1].Value.Trim().Trim('"', '\'');
            string addr = angleMatch.Groups[2].Value.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(name)) name = addr;
            return (name, addr);
        }

        string emailOnly = ExtractEmailAddress(decoded);
        return (emailOnly, emailOnly);
    }

    public static string ExtractEmailAddress(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var m = Regex.Match(raw, @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}");
        if (m.Success) return m.Value.ToLowerInvariant();
        if (raw.Contains('@')) return raw.Trim('<', '>', ' ', '"').ToLowerInvariant();
        return raw.Trim();
    }

    private static List<string> ParseAddressList(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(DecodeHeaderWords)
            .ToList();
    }

    public static string StripHtmlTags(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        string withoutStyleScript = Regex.Replace(html, @"<(script|style)[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        string withBreaks = Regex.Replace(withoutStyleScript, @"<(br|p|div|tr|li)[^>]*>", "\n", RegexOptions.IgnoreCase);
        string plain = Regex.Replace(withBreaks, @"<[^>]+>", "");
        return System.Net.WebUtility.HtmlDecode(plain).Trim();
    }
}

