using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace PixelHelper;

public enum WebGuardVerdict
{
    Safe,
    Warning,
    Blocked
}

public sealed record WebGuardResult(
    string Url,
    string Host,
    WebGuardVerdict Verdict,
    string Reason,
    string? Category = null,
    string? MatchedBrand = null
);

public static class WebGuard
{
    private static readonly HashSet<string> DefaultTrustedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "google.com", "www.google.com", "google.ru", "www.google.ru", "gmail.com", "www.gmail.com", "googlemail.com", "mail.google.com", "accounts.google.com", "drive.google.com", "docs.google.com",
        "yandex.ru", "www.yandex.ru", "ya.ru", "yandex.kz", "www.yandex.kz", "dzen.ru",
        "mail.ru", "e.mail.ru", "cloud.mail.ru", "inbox.ru", "bk.ru", "list.ru", "internet.ru", "outlook.com", "hotmail.com", "live.com",
        "vk.com", "m.vk.com",
        "gosuslugi.ru", "esia.gosuslugi.ru", "gu-st.ru",
        "egov.kz", "my.egov.kz",
        "nalog.gov.ru", "lkfl2.nalog.ru", "mos.ru",
        "sberbank.ru", "online.sberbank.ru", "sber.ru",
        "tbank.ru", "tinkoff.ru",
        "vtb.ru", "online.vtb.ru",
        "kaspi.kz", "damumed.kz", "kolesa.kz", "krisha.kz",
        "youtube.com", "www.youtube.com", "youtu.be",
        "wikipedia.org", "ru.wikipedia.org",
        "github.com", "microsoft.com", "support.microsoft.com",
        "anydesk.com", "www.anydesk.com",
        "helper.gp1.loc"
    };

    private static readonly string[] SensitiveBrands =
    {
        "sberbank", "sber", "gosuslugi", "tinkoff", "tbank", "alfabank", "vtb",
        "kaspi", "halyk", "damumed", "egov", "nalog", "yandex", "google", "mail",
        "telegram", "whatsapp", "anydesk", "teamviewer", "rustdesk"
    };

    private static readonly HashSet<string> SuspiciousTlds = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xyz", ".top", ".click", ".buzz", ".fit", ".tk", ".ml", ".ga", ".cf", ".gq",
        ".monster", ".rest", ".work", ".kim", ".country", ".stream"
    };

    // Cyrillic characters that look identical or nearly identical to Latin characters
    private static readonly Dictionary<char, char> CyrillicHomoglyphs = new()
    {
        ['\u0430'] = 'a', ['\u0441'] = 'c', ['\u0435'] = 'e', ['\u043e'] = 'o',
        ['\u0440'] = 'p', ['\u0445'] = 'x', ['\u0443'] = 'y', ['\u0456'] = 'i',
        ['\u0458'] = 'j', ['\u0455'] = 's', ['\u0410'] = 'A', ['\u0412'] = 'B',
        ['\u0421'] = 'C', ['\u0415'] = 'E', ['\u041d'] = 'H', ['\u041e'] = 'O',
        ['\u0420'] = 'P', ['\u0422'] = 'T', ['\u0425'] = 'X'
    };

    public static WebGuardResult Check(string rawUrl, Settings? settings = null)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
            return new WebGuardResult("", "", WebGuardVerdict.Safe, "Пустой URL");

        string normalizedUrl = rawUrl.Trim();
        if (!normalizedUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalizedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalizedUrl = "https://" + normalizedUrl;
        }

        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out Uri? uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            return new WebGuardResult(rawUrl, "", WebGuardVerdict.Warning, "Некорректный или повреждённый формат ссылки", "InvalidUri");
        }

        string host = uri.Host.TrimEnd('.').ToLowerInvariant();

        // 1. Whitelist check (0 ms)
        if (IsWhitelisted(host, uri, settings))
        {
            return new WebGuardResult(normalizedUrl, host, WebGuardVerdict.Safe, "Доверенный адрес из белого списка", "Whitelist");
        }

        // 2. Blacklist check
        if (settings?.WebGuardBlacklist != null)
        {
            foreach (var black in settings.WebGuardBlacklist)
            {
                if (string.IsNullOrWhiteSpace(black)) continue;
                string b = black.Trim().ToLowerInvariant();
                if (host == b || host.EndsWith("." + b, StringComparison.OrdinalIgnoreCase))
                {
                    return new WebGuardResult(normalizedUrl, host, WebGuardVerdict.Blocked, "Адрес заблокирован политикой безопасности организации", "Blacklist", b);
                }
            }
        }

        // 3. Homograph Attack (Mixed Cyrillic / Latin or Punycode IDN)
        if (host.StartsWith("xn--", StringComparison.OrdinalIgnoreCase) || host.Contains(".xn--", StringComparison.OrdinalIgnoreCase))
        {
            // IDN Punycode domain
            try
            {
                var idn = new IdnMapping();
                string unicodeHost = idn.GetUnicode(host);
                if (ContainsHomoglyphs(unicodeHost, out string matchedBrand))
                {
                    return new WebGuardResult(normalizedUrl, host, WebGuardVerdict.Blocked,
                        $"Обнаружена маскировка под известный бренд через Punycode: «{unicodeHost}»", "Homograph", matchedBrand);
                }
            }
            catch { }
        }

        if (ContainsHomoglyphs(host, out string homoglyphBrand))
        {
            return new WebGuardResult(normalizedUrl, host, WebGuardVerdict.Blocked,
                "Обнаружена подмена символов (гомографическая атака): буквы кириллицы в домене", "Homograph", homoglyphBrand);
        }

        // 4. Raw public IP address
        if (IPAddress.TryParse(host, out IPAddress? ip))
        {
            if (!IsPrivateIp(ip))
            {
                return new WebGuardResult(normalizedUrl, host, WebGuardVerdict.Warning,
                    "Переход по прямому IP-адресу вместо доменного имени", "RawIp");
            }
        }

        // 5. Deceptive subdomain (e.g. sberbank.ru.evil-scam.com)
        foreach (var brand in SensitiveBrands)
        {
            if (host.StartsWith(brand + ".", StringComparison.OrdinalIgnoreCase) ||
                host.Contains("." + brand + ".", StringComparison.OrdinalIgnoreCase))
            {
                string baseDomain = GetBaseDomain(host);
                if (!baseDomain.Equals(brand + ".ru", StringComparison.OrdinalIgnoreCase) &&
                    !baseDomain.Equals(brand + ".kz", StringComparison.OrdinalIgnoreCase) &&
                    !baseDomain.Equals(brand + ".com", StringComparison.OrdinalIgnoreCase) &&
                    !baseDomain.Equals(brand + ".gov.ru", StringComparison.OrdinalIgnoreCase))
                {
                    return new WebGuardResult(normalizedUrl, host, WebGuardVerdict.Warning,
                        $"Сайт маскируется под сервис «{brand}» через сторонний домен {baseDomain}", "DeceptiveSubdomain", brand);
                }
            }
        }

        // 6. Typosquatting and Leetspeak check (Levenshtein distance & digit substitutions)
        string labelWithoutTld = GetSecondLevelDomain(host);
        string leetNormalized = NormalizeLeetspeak(labelWithoutTld);
        foreach (var brand in SensitiveBrands)
        {
            if (labelWithoutTld.Equals(brand, StringComparison.OrdinalIgnoreCase)) continue;
            if (brand == "mail" && (labelWithoutTld.Equals("gmail", StringComparison.OrdinalIgnoreCase) ||
                                    labelWithoutTld.Equals("email", StringComparison.OrdinalIgnoreCase) ||
                                    labelWithoutTld.Equals("googlemail", StringComparison.OrdinalIgnoreCase) ||
                                    labelWithoutTld.Equals("webmail", StringComparison.OrdinalIgnoreCase))) continue;

            if (leetNormalized.Equals(brand, StringComparison.OrdinalIgnoreCase))
            {
                return new WebGuardResult(normalizedUrl, host, WebGuardVerdict.Blocked,
                    $"Обнаружена маскировка под известный бренд через замену букв цифрами (Leetspeak: «{brand}»)", "Leetspeak", brand);
            }

            int dist = LevenshteinDistance(labelWithoutTld, brand);
            if (dist > 0 && dist <= 2 && Math.Abs(labelWithoutTld.Length - brand.Length) <= 2)
            {
                return new WebGuardResult(normalizedUrl, host, WebGuardVerdict.Warning,
                    $"Подозрительное сходство с сервисом «{brand}» (возможная опечатка/подделка)", "Typosquatting", brand);
            }

            // Also check brand with hyphenated suffix (e.g. sberbank-login, gosuslugi-cabinet)
            if (labelWithoutTld.StartsWith(brand + "-", StringComparison.OrdinalIgnoreCase) ||
                labelWithoutTld.EndsWith("-" + brand, StringComparison.OrdinalIgnoreCase))
            {
                return new WebGuardResult(normalizedUrl, host, WebGuardVerdict.Warning,
                    $"Домен содержит имя «{brand}» с подозрительными приставками", "BrandPrefix", brand);
            }
        }

        // 7. Suspicious TLD check
        foreach (var tld in SuspiciousTlds)
        {
            if (host.EndsWith(tld, StringComparison.OrdinalIgnoreCase))
            {
                if (Regex.IsMatch(host, "(login|auth|cabinet|bank|pay|secure|support|account|bonus|verify|nalog)"))
                {
                    return new WebGuardResult(normalizedUrl, host, WebGuardVerdict.Warning,
                        $"Сайт в зоне {tld} с ключевыми словами авторизации/платежей", "SuspiciousTld");
                }
            }
        }

        return new WebGuardResult(normalizedUrl, host, WebGuardVerdict.Safe, "Проверка безопасности пройдена");
    }

    public static bool IsWhitelisted(string host, Uri uri, Settings? settings)
    {
        // Localhost and loopback
        if (host == "localhost" || host == "127.0.0.1" || host == "::1") return true;

        // Corporate local domains
        if (host.EndsWith(".loc", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Server URL host
        if (!string.IsNullOrWhiteSpace(settings?.ServerUrl) &&
            Uri.TryCreate(settings.ServerUrl, UriKind.Absolute, out Uri? serverUri) &&
            host.Equals(serverUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Private IP
        if (IPAddress.TryParse(host, out IPAddress? ip) && IsPrivateIp(ip))
        {
            return true;
        }

        // Predefined trusted domains and subdomains
        if (DefaultTrustedHosts.Contains(host)) return true;
        foreach (var trusted in DefaultTrustedHosts)
        {
            if (host.EndsWith("." + trusted, StringComparison.OrdinalIgnoreCase)) return true;
        }

        // Custom whitelist from settings
        if (settings?.WebGuardWhitelist != null)
        {
            foreach (var entry in settings.WebGuardWhitelist)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                string clean = entry.Trim().ToLowerInvariant();
                if (host == clean || host.EndsWith("." + clean, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static bool IsPrivateIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        byte[] bytes = ip.GetAddressBytes();
        if (bytes.Length == 4)
        {
            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // 169.254.0.0/16 (link local)
            if (bytes[0] == 169 && bytes[1] == 254) return true;
        }
        return false;
    }

    private static bool ContainsHomoglyphs(string str, out string matchedBrand)
    {
        matchedBrand = "";
        bool hasCyrillic = false;
        bool hasLatin = false;
        var latinNormalized = new char[str.Length];

        for (int i = 0; i < str.Length; i++)
        {
            char c = str[i];
            if (CyrillicHomoglyphs.TryGetValue(c, out char latinRepl))
            {
                hasCyrillic = true;
                latinNormalized[i] = latinRepl;
            }
            else
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) hasLatin = true;
                latinNormalized[i] = char.ToLowerInvariant(c);
            }
        }

        if (hasCyrillic && hasLatin)
        {
            string converted = new string(latinNormalized);
            foreach (var brand in SensitiveBrands)
            {
                if (converted.Contains(brand))
                {
                    matchedBrand = brand;
                    return true;
                }
            }
        }

        return false;
    }

    private static string GetSecondLevelDomain(string host)
    {
        var parts = host.Split('.');
        if (parts.Length >= 2)
        {
            // If domain is like something.co.uk or something.gov.ru
            if (parts.Length >= 3 && (parts[^1].Length <= 3 && (parts[^2] == "gov" || parts[^2] == "co" || parts[^2] == "com" || parts[^2] == "org")))
            {
                return parts[^3];
            }
            return parts[^2];
        }
        return host;
    }

    private static string GetBaseDomain(string host)
    {
        var parts = host.Split('.');
        if (parts.Length >= 2)
        {
            if (parts.Length >= 3 && (parts[^2] == "gov" || parts[^2] == "co" || parts[^2] == "com" || parts[^2] == "edu"))
            {
                return string.Join(".", parts.TakeLast(3));
            }
            return string.Join(".", parts.TakeLast(2));
        }
        return host;
    }

    public static int LevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return t?.Length ?? 0;
        if (string.IsNullOrEmpty(t)) return s.Length;

        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }

    private static string NormalizeLeetspeak(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var chars = input.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                '0' => 'o',
                '1' => 'l',
                '3' => 'e',
                '4' or '@' => 'a',
                '5' => 's',
                '7' => 't',
                _ => char.ToLowerInvariant(chars[i])
            };
        }
        return new string(chars);
    }

    public static bool LaunchUrl(string url, Settings? settings = null)
    {
        try
        {
            settings ??= Settings.Load();

            string? targetBrowser = settings?.PreferredBrowserPath;
            if (!string.IsNullOrWhiteSpace(targetBrowser) && File.Exists(targetBrowser))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = targetBrowser,
                    Arguments = $"\"{url}\"",
                    UseShellExecute = false
                });
                return true;
            }

            // Fallback: detect an installed browser that is NOT PixelHelper
            var browsers = BrowserDetector.GetInstalledBrowsers();
            var defaultBrowser = browsers.FirstOrDefault(b => !b.ExecutablePath.Contains("PixelHelper", StringComparison.OrdinalIgnoreCase));
            if (defaultBrowser != null && File.Exists(defaultBrowser.ExecutablePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = defaultBrowser.ExecutablePath,
                    Arguments = $"\"{url}\"",
                    UseShellExecute = false
                });
                return true;
            }

            // Ultimate fallback
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Settings.Log(ex);
            return false;
        }
    }
}
