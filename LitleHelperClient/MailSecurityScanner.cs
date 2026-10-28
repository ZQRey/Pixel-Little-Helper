using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PixelHelper;

public static class MailSecurityScanner
{
    private const string EicarSignature = @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

    private static readonly string[] SpamPatterns =
    [
        "вы выиграли", "получите выигрыш", "срочно подтвердите пароль", "ваш аккаунт будет заблокирован",
        "переведите средства", "безопасный счет", "криптовалютный бонус", "быстрый заработок без вложений",
        "casino", "jackpot", "bitcoin giveaway", "urgent account verification", "wire transfer immediately",
        "нажмите здесь для разблокировки", "100% гарантия дохода", "ликвидация склада скидки 90%"
    ];

    private static readonly Dictionary<string, string[]> BrandDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        ["google"] = ["google.com", "gmail.com", "googlemail.com"],
        ["гугл"] = ["google.com", "gmail.com", "googlemail.com"],
        ["сбер"] = ["sberbank.ru", "sber.ru"],
        ["sber"] = ["sberbank.ru", "sber.ru"],
        ["госуслуг"] = ["gosuslugi.ru", "gu-st.ru"],
        ["gosuslugi"] = ["gosuslugi.ru", "gu-st.ru"],
        ["kaspi"] = ["kaspi.kz"],
        ["каспи"] = ["kaspi.kz"],
        ["egov"] = ["egov.kz", "nitec.kz"],
        ["damumed"] = ["damumed.kz"],
        ["дамумед"] = ["damumed.kz"],
        ["налоговая"] = ["nalog.gov.ru", "kgd.gov.kz"],
        ["microsoft"] = ["microsoft.com", "office.com", "outlook.com"],
        ["яндекс"] = ["yandex.ru", "ya.ru", "yandex.kz"]
    };

    public static bool IsSenderBlocked(string emailAddress, Settings settings)
    {
        if (string.IsNullOrWhiteSpace(emailAddress)) return false;
        string cleanEmail = MailProtocolClient.ExtractEmailAddress(emailAddress);
        string domain = cleanEmail.Contains('@') ? cleanEmail.Split('@').Last() : cleanEmail;

        foreach (string entry in settings.MailBlockedSenders)
        {
            if (MatchesRule(cleanEmail, domain, entry)) return true;
        }
        return false;
    }

    public static bool IsSenderTrusted(string emailAddress, Settings settings)
    {
        if (string.IsNullOrWhiteSpace(emailAddress)) return false;
        string cleanEmail = MailProtocolClient.ExtractEmailAddress(emailAddress);
        string domain = cleanEmail.Contains('@') ? cleanEmail.Split('@').Last() : cleanEmail;

        if (domain.Equals("gp1.loc", StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith(".gp1.loc", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (string entry in settings.MailTrustedSenders)
        {
            if (MatchesRule(cleanEmail, domain, entry)) return true;
        }
        return false;
    }

    private static bool MatchesRule(string email, string domain, string rawRule)
    {
        if (string.IsNullOrWhiteSpace(rawRule)) return false;
        string rule = rawRule.Trim().ToLowerInvariant();
        if (rule.StartsWith("*@")) rule = rule[1..]; // "@domain.com"

        if (rule.StartsWith("@"))
        {
            string ruleDomain = rule[1..];
            return domain.Equals(ruleDomain, StringComparison.OrdinalIgnoreCase) ||
                   domain.EndsWith("." + ruleDomain, StringComparison.OrdinalIgnoreCase);
        }

        if (!rule.Contains('@'))
        {
            return domain.Equals(rule, StringComparison.OrdinalIgnoreCase) ||
                   domain.EndsWith("." + rule, StringComparison.OrdinalIgnoreCase);
        }

        return email.Equals(rule, StringComparison.OrdinalIgnoreCase);
    }

    public static bool AddToBlacklist(string emailOrDomain, Settings settings)
    {
        string normalized = NormalizeEntry(emailOrDomain);
        if (string.IsNullOrEmpty(normalized)) return false;

        settings.MailTrustedSenders.RemoveAll(x => string.Equals(NormalizeEntry(x), normalized, StringComparison.OrdinalIgnoreCase));
        if (!settings.MailBlockedSenders.Any(x => string.Equals(NormalizeEntry(x), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            settings.MailBlockedSenders.Add(normalized);
            settings.Save();
            return true;
        }
        return false;
    }

    public static bool AddToWhitelist(string emailOrDomain, Settings settings)
    {
        string normalized = NormalizeEntry(emailOrDomain);
        if (string.IsNullOrEmpty(normalized)) return false;

        settings.MailBlockedSenders.RemoveAll(x => string.Equals(NormalizeEntry(x), normalized, StringComparison.OrdinalIgnoreCase));
        if (!settings.MailTrustedSenders.Any(x => string.Equals(NormalizeEntry(x), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            settings.MailTrustedSenders.Add(normalized);
            settings.Save();
            return true;
        }
        return false;
    }

    public static bool RemoveFromBlacklist(string emailOrDomain, Settings settings)
    {
        string normalized = NormalizeEntry(emailOrDomain);
        int removed = settings.MailBlockedSenders.RemoveAll(x => string.Equals(NormalizeEntry(x), normalized, StringComparison.OrdinalIgnoreCase));
        if (removed > 0) settings.Save();
        return removed > 0;
    }

    public static bool RemoveFromWhitelist(string emailOrDomain, Settings settings)
    {
        string normalized = NormalizeEntry(emailOrDomain);
        int removed = settings.MailTrustedSenders.RemoveAll(x => string.Equals(NormalizeEntry(x), normalized, StringComparison.OrdinalIgnoreCase));
        if (removed > 0) settings.Save();
        return removed > 0;
    }

    public static string NormalizeEntry(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string clean = raw.Trim().ToLowerInvariant();
        if (clean.StartsWith("*@")) clean = clean[1..];
        if (clean.Contains('<') && clean.Contains('>'))
            clean = MailProtocolClient.ExtractEmailAddress(clean);
        return clean;
    }

    public static async Task EvaluateMessageAsync(
        MailMessageItem message,
        List<(MailAttachmentItem Item, byte[] RawBytes)> attachments,
        Settings settings,
        AntivirusService? antivirus,
        CancellationToken token = default)
    {
        message.PhishingWarnings.Clear();
        bool isBlockedSender = IsSenderBlocked(message.FromAddress, settings);
        bool isTrustedSender = !isBlockedSender && IsSenderTrusted(message.FromAddress, settings);

        // 1. Scan all attachments with PixelGuard AV FIRST (even for trusted senders, malware is never allowed!)
        bool malwareDetected = false;
        var malwareNames = new List<string>();

        if (settings.MailScanAntivirus && attachments.Count > 0)
        {
            foreach (var (attItem, rawBytes) in attachments)
            {
                var scanRes = await ScanAttachmentBytesAsync(attItem.FileName, rawBytes, settings, antivirus, token);
                attItem.Sha256 = scanRes.Sha256;
                if (scanRes.IsThreat)
                {
                    attItem.IsThreat = true;
                    attItem.ThreatName = scanRes.ThreatName;
                    attItem.ThreatType = scanRes.ThreatType;
                    attItem.QuarantineId = scanRes.QuarantineId;
                    malwareDetected = true;
                    malwareNames.Add($"{attItem.FileName} ({scanRes.ThreatName})");
                }
            }
        }

        if (malwareDetected)
        {
            message.SecurityVerdict = MailSecurityVerdict.BlockedMalware;
            message.SecurityReason = "🚨 PixelGuard AV заблокировал вредоносное вложение: " + string.Join(", ", malwareNames);
            message.Folder = MailFolderKind.Spam;
            return;
        }

        // 2. Blacklist check
        if (isBlockedSender)
        {
            message.SecurityVerdict = MailSecurityVerdict.BlockedBlacklist;
            message.SecurityReason = $"🚫 Адрес отправителя ({message.FromAddress}) находится в чёрном списке";
            message.Folder = MailFolderKind.Spam;
            return;
        }

        // 3. Phishing analysis (sender spoofing, SPF/DKIM/DMARC, homographs, malicious links via WebGuard)
        if (settings.MailScanPhishing)
        {
            var phishingIssues = AnalyzePhishing(message, settings);
            if (phishingIssues.Count > 0)
            {
                message.PhishingWarnings.AddRange(phishingIssues);
            }
        }

        if (message.PhishingWarnings.Count > 0)
        {
            message.SecurityVerdict = MailSecurityVerdict.PhishingWarning;
            message.SecurityReason = "⚠️ PixelGuard AV обнаружил признаки фишинга: " + message.PhishingWarnings[0];
            if (!isTrustedSender && message.PhishingWarnings.Any(w => w.Contains("Заблокирована ссылка") || w.Contains("подделка домена") || w.Contains("SPF=fail")))
            {
                message.Folder = MailFolderKind.Spam;
            }
            return;
        }

        // 4. Trusted Whitelist
        if (isTrustedSender)
        {
            message.SecurityVerdict = MailSecurityVerdict.TrustedWhitelist;
            message.SecurityReason = $"✅ Доверенный отправитель из белого списка ({message.FromAddress}) · Вложения проверены PixelGuard AV";
            return;
        }

        // 5. Spam Classifier
        if (settings.MailFilterSpam)
        {
            int spamScore = CalculateSpamScore(message);
            message.SpamScore = spamScore;
            if (spamScore >= 60)
            {
                message.SecurityVerdict = MailSecurityVerdict.Spam;
                message.SecurityReason = $"🛡️ Письмо определено как спам (индекс спама: {spamScore}/100)";
                message.Folder = MailFolderKind.Spam;
                return;
            }
        }

        message.SecurityVerdict = MailSecurityVerdict.Clean;
        message.SecurityReason = "🛡️ Проверено PixelGuard AV: вирусов, фишинга и спама не обнаружено";
    }

    public static async Task<(bool IsThreat, string ThreatName, string ThreatType, string Sha256, string QuarantineId)> ScanAttachmentBytesAsync(
        string fileName,
        byte[] content,
        Settings settings,
        AntivirusService? antivirus,
        CancellationToken token = default)
    {
        string sha256 = Convert.ToHexString(SHA256.HashData(content));
        string safeName = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "attachment.bin" : fileName);
        string lowerName = safeName.ToLowerInvariant();
        string ext = Path.GetExtension(lowerName);

        // 1. Check EICAR Standard Antivirus Test Signature
        if (content.Length <= 1024 * 1024)
        {
            string ascii = Encoding.ASCII.GetString(content);
            if (ascii.Contains(EicarSignature, StringComparison.Ordinal))
            {
                string qId = IsolateAttachmentToQuarantine(safeName, content, "EICAR-Test-File", "Signature", settings, antivirus);
                return (true, "EICAR-Test-File", "Signature", sha256, qId);
            }
        }

        // 2. Check disguised double extension in attachment name (e.g. "Счет_на_оплату.pdf.exe", "Акт.docx.scr")
        string withoutExt = Path.GetFileNameWithoutExtension(lowerName);
        if (withoutExt.EndsWith(".pdf") || withoutExt.EndsWith(".doc") || withoutExt.EndsWith(".docx") ||
            withoutExt.EndsWith(".xls") || withoutExt.EndsWith(".xlsx") || withoutExt.EndsWith(".jpg") ||
            withoutExt.EndsWith(".png") || withoutExt.EndsWith(".txt") || withoutExt.EndsWith(".rtf"))
        {
            if (ext is ".exe" or ".scr" or ".vbs" or ".bat" or ".cmd" or ".ps1" or ".js" or ".hta" or ".pif" or ".com" or ".jar")
            {
                string qId = IsolateAttachmentToQuarantine(safeName, content, "Trojan.Mail.DoubleExtension", "Heuristic", settings, antivirus);
                return (true, "Trojan.Mail.DoubleExtension", "Heuristic", sha256, qId);
            }
        }

        // 3. Direct dangerous executable/script attachments in mail
        if (ext is ".scr" or ".pif" or ".hta" or ".vbs" or ".wsf")
        {
            string qId = IsolateAttachmentToQuarantine(safeName, content, "Malware.Mail.ExecutableScript", "Heuristic", settings, antivirus);
            return (true, "Malware.Mail.ExecutableScript", "Heuristic", sha256, qId);
        }

        // 4. Inspect inside ZIP archives for disguised executables or EICAR
        if (ext == ".zip" && content.Length > 0 && content.Length <= 50 * 1024 * 1024)
        {
            try
            {
                using var ms = new MemoryStream(content);
                using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
                foreach (var entry in zip.Entries.Take(100))
                {
                    string entryLower = entry.Name.ToLowerInvariant();
                    string entryExt = Path.GetExtension(entryLower);
                    string entryBase = Path.GetFileNameWithoutExtension(entryLower);
                    if ((entryBase.EndsWith(".pdf") || entryBase.EndsWith(".doc") || entryBase.EndsWith(".docx") || entryBase.EndsWith(".xlsx")) &&
                        entryExt is ".exe" or ".scr" or ".vbs" or ".bat" or ".cmd" or ".ps1" or ".js" or ".hta")
                    {
                        string qId = IsolateAttachmentToQuarantine(safeName, content, "Trojan.Archive.DisguisedExe", "ArchiveHeuristic", settings, antivirus);
                        return (true, "Trojan.Archive.DisguisedExe", "ArchiveHeuristic", sha256, qId);
                    }
                    if (entry.Length > 0 && entry.Length <= 131072)
                    {
                        using var es = entry.Open();
                        using var sr = new StreamReader(es, Encoding.ASCII);
                        string entryText = await sr.ReadToEndAsync(token);
                        if (entryText.Contains(EicarSignature, StringComparison.Ordinal))
                        {
                            string qId = IsolateAttachmentToQuarantine(safeName, content, "EICAR-Test-File (Archive)", "Signature", settings, antivirus);
                            return (true, "EICAR-Test-File (Archive)", "Signature", sha256, qId);
                        }
                    }
                }
            }
            catch { }
        }

        // 5. Run full AntivirusService file scan (MalwareBazaar SHA-256 database + script dropper heuristics)
        string tempDir = Path.Combine(Path.GetTempPath(), "PixelHelperMailScan");
        Directory.CreateDirectory(tempDir);
        string tempFile = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + "_" + safeName);
        try
        {
            await File.WriteAllBytesAsync(tempFile, content, token);
            if (antivirus != null)
            {
                var res = await antivirus.ScanFileAsync(tempFile, token);
                if (res.IsThreat)
                {
                    var qItem = antivirus.Quarantine.QuarantineFile(tempFile, res.ThreatName, res.ThreatType, settings.AntivirusRetentionDays);
                    return (true, res.ThreatName, res.ThreatType, sha256, qItem?.Id ?? "");
                }
            }
            else
            {
                // Standalone heuristic check for script droppers
                if (ext is ".ps1" or ".bat" or ".cmd" or ".js" && content.Length < 1048576)
                {
                    string scriptText = Encoding.UTF8.GetString(content);
                    if (scriptText.Contains("-enc", StringComparison.OrdinalIgnoreCase) ||
                        scriptText.Contains("WScript.Shell", StringComparison.OrdinalIgnoreCase))
                    {
                        string qId = IsolateAttachmentToQuarantine(safeName, content, "Dropper.Mail.Script", "Heuristic", settings, null);
                        return (true, "Dropper.Mail.Script", "Heuristic", sha256, qId);
                    }
                }
            }
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }

        return (false, "", "", sha256, "");
    }

    private static string IsolateAttachmentToQuarantine(
        string fileName,
        byte[] content,
        string threatName,
        string threatType,
        Settings settings,
        AntivirusService? antivirus)
    {
        try
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PixelHelperMailQuarantine");
            Directory.CreateDirectory(tempDir);
            string tempFile = Path.Combine(tempDir, fileName);
            File.WriteAllBytes(tempFile, content);

            var qm = antivirus?.Quarantine ?? new QuarantineManager();
            var item = qm.QuarantineFile(tempFile, threatName, threatType, settings.AntivirusRetentionDays);
            return item?.Id ?? "";
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
            return "";
        }
    }

    public static List<string> AnalyzePhishing(MailMessageItem message, Settings settings)
    {
        var warnings = new List<string>();
        string fromAddr = (message.FromAddress ?? "").Trim().ToLowerInvariant();
        string fromName = (message.FromName ?? "").Trim();
        string senderDomain = fromAddr.Contains('@') ? fromAddr.Split('@').Last() : "";

        // 1. Check sender domain with WebGuard (homographs, Punycode, typosquatting, blacklist)
        if (!string.IsNullOrWhiteSpace(senderDomain))
        {
            var domainCheck = WebGuard.Check("https://" + senderDomain, settings);
            if (domainCheck.Verdict == WebGuardVerdict.Blocked || domainCheck.Verdict == WebGuardVerdict.Warning)
            {
                warnings.Add($"Подозрительный домен отправителя ({senderDomain}): {domainCheck.Reason}");
            }
        }

        // 2. Display name spoofing: DisplayName contains a different email address
        string embeddedEmail = MailProtocolClient.ExtractEmailAddress(fromName);
        if (!string.IsNullOrEmpty(embeddedEmail) && embeddedEmail.Contains('@') &&
            !string.Equals(embeddedEmail, fromAddr, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Подмена имени отправителя: в имени указан «{embeddedEmail}», но реальный адрес «{fromAddr}»");
        }

        // 3. Brand impersonation in display name
        foreach (var (brandKey, officialDomains) in BrandDomains)
        {
            if (fromName.Contains(brandKey, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(senderDomain))
            {
                bool matchesOfficial = officialDomains.Any(d =>
                    senderDomain.Equals(d, StringComparison.OrdinalIgnoreCase) ||
                    senderDomain.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
                if (!matchesOfficial)
                {
                    warnings.Add($"Возможная подделка организации «{fromName}» со стороннего домена {senderDomain}");
                    break;
                }
            }
        }

        // 4. Reply-To mismatch
        if (!string.IsNullOrWhiteSpace(message.ReplyToAddress))
        {
            string replyAddr = MailProtocolClient.ExtractEmailAddress(message.ReplyToAddress);
            string replyDomain = replyAddr.Contains('@') ? replyAddr.Split('@').Last() : "";
            if (!string.IsNullOrEmpty(replyDomain) && !string.IsNullOrEmpty(senderDomain) &&
                !string.Equals(replyDomain, senderDomain, StringComparison.OrdinalIgnoreCase))
            {
                bool isAlias = (senderDomain is "gmail.com" or "googlemail.com" && replyDomain is "gmail.com" or "googlemail.com") ||
                               (senderDomain.StartsWith("yandex.") && replyDomain.StartsWith("yandex.") || (senderDomain is "ya.ru" && replyDomain.StartsWith("yandex.")));
                if (!isAlias)
                {
                    warnings.Add($"Адрес для ответа ({replyAddr}) отличается от домена отправителя ({senderDomain})");
                }
            }
        }

        // 5. SPF / DKIM / DMARC failure in headers
        if (!string.IsNullOrWhiteSpace(message.HeadersRaw))
        {
            if (Regex.IsMatch(message.HeadersRaw, @"\bspf=fail\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(message.HeadersRaw, @"Received-SPF:\s*fail\b", RegexOptions.IgnoreCase))
            {
                warnings.Add("Проверка подлинности сервера отправителя не пройдена (SPF=fail)");
            }
            if (Regex.IsMatch(message.HeadersRaw, @"\bdkim=fail\b", RegexOptions.IgnoreCase))
                warnings.Add("Цифровая подпись DKIM нарушена (DKIM=fail)");
            if (Regex.IsMatch(message.HeadersRaw, @"\bdmarc=fail\b", RegexOptions.IgnoreCase))
                warnings.Add("Политика безопасности домена нарушена (DMARC=fail)");
        }

        // 6. Scan URLs inside HTML and plain text body using WebGuard
        if (!string.IsNullOrWhiteSpace(message.BodyHtml))
        {
            var anchorMatches = Regex.Matches(message.BodyHtml, @"<a\s+[^>]*href\s*=\s*[""']([^""']+)[""'][^>]*>([\s\S]*?)</a>", RegexOptions.IgnoreCase);
            foreach (Match m in anchorMatches)
            {
                string href = m.Groups[1].Value.Trim();
                string visibleText = MailProtocolClient.StripHtmlTags(m.Groups[2].Value).Trim();
                if (!href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    continue;

                var urlRes = WebGuard.Check(href, settings);
                if (urlRes.Verdict == WebGuardVerdict.Blocked)
                {
                    warnings.Add($"Заблокирована фишинговая ссылка ({urlRes.Host}): {urlRes.Reason}");
                }
                else if (urlRes.Verdict == WebGuardVerdict.Warning)
                {
                    warnings.Add($"Подозрительная ссылка в письме ({urlRes.Host}): {urlRes.Reason}");
                }

                if ((visibleText.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     visibleText.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
                    Uri.TryCreate(visibleText, UriKind.Absolute, out var visibleUri) &&
                    Uri.TryCreate(href, UriKind.Absolute, out var actualUri) &&
                    !string.Equals(visibleUri.Host, actualUri.Host, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"Обманная ссылка: текст «{visibleUri.Host}» ведёт на «{actualUri.Host}»");
                }
            }
        }

        string textToScan = (message.BodyText ?? "") + "\n" + (message.BodyHtml ?? "");
        var rawUrls = Regex.Matches(textToScan, @"https?://[^\s""'<>]+", RegexOptions.IgnoreCase);
        foreach (Match m in rawUrls)
        {
            string url = m.Value.TrimEnd('.', ',', ';', ')', ']');
            var urlRes = WebGuard.Check(url, settings);
            if (urlRes.Verdict == WebGuardVerdict.Blocked)
            {
                string msg = $"Заблокирована ссылка ({urlRes.Host}): {urlRes.Reason}";
                if (!warnings.Contains(msg)) warnings.Add(msg);
            }
            else if (urlRes.Verdict == WebGuardVerdict.Warning)
            {
                string msg = $"Подозрительная ссылка ({urlRes.Host}): {urlRes.Reason}";
                if (!warnings.Contains(msg)) warnings.Add(msg);
            }
        }

        return warnings;
    }

    public static int CalculateSpamScore(MailMessageItem message)
    {
        int score = 0;
        string combined = ((message.Subject ?? "") + " " + (message.BodyText ?? "")).ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(message.HeadersRaw) &&
            Regex.IsMatch(message.HeadersRaw, @"X-Spam-Flag:\s*YES", RegexOptions.IgnoreCase))
        {
            score += 75;
        }

        foreach (string pattern in SpamPatterns)
        {
            if (combined.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                score += 35;
            }
        }

        // Check excessive CAPS in Subject
        string subj = message.Subject ?? "";
        int letters = subj.Count(char.IsLetter);
        int upper = subj.Count(char.IsUpper);
        if (letters >= 10 && (double)upper / letters > 0.75)
        {
            score += 25;
        }

        // Multiple exclamation/dollar/ruble marks
        if (subj.Count(c => c is '!' or '$' or '₽' or '€') >= 3)
        {
            score += 20;
        }

        // Suspicious TLD of sender
        string fromAddr = (message.FromAddress ?? "").ToLowerInvariant();
        if (fromAddr.EndsWith(".xyz") || fromAddr.EndsWith(".top") || fromAddr.EndsWith(".click") ||
            fromAddr.EndsWith(".monster") || fromAddr.EndsWith(".buzz") || fromAddr.EndsWith(".loan"))
        {
            score += 30;
        }

        return Math.Min(100, score);
    }
}
