using System.Globalization;
using System.Text.Json.Serialization;

namespace LitleHelperServer;

/// <summary>One reporting backend owns one GLPI source. Frozen at startup, never changes mid-report.</summary>
public sealed record ReportDeployment(string TimeZoneId, string Locale, string OrganizationName,
    int[] ServiceUserIds, string[] ServiceLogins, int[] TechnicianProfileIds)
{
    [JsonIgnore] public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
    [JsonIgnore] public CultureInfo Culture => CultureInfo.GetCultureInfo(Locale);
    public static ReportDeployment Current { get; private set; } =
        new("Asia/Qyzylorda", "ru-KZ", "", [], [], []);

    public static void Configure(IConfiguration configuration)
        => Current = Parse(configuration);

    public static ReportDeployment Parse(IConfiguration configuration)
    {
        var section = configuration.GetSection("GlpiReports:Organization");
        var value = new ReportDeployment(section["TimeZone"] ?? "Asia/Qyzylorda",
            section["Locale"] ?? "ru-KZ", section["Name"] ?? "",
            section.GetSection("ServiceUserIds").Get<int[]>() ?? [],
            section.GetSection("ServiceLogins").Get<string[]>() ?? [],
            section.GetSection("TechnicianProfileIds").Get<int[]>() ?? []);
        _ = value.Zone; _ = value.Culture;
        if (value.OrganizationName.Length > 255 || value.ServiceUserIds.Any(i=>i<1)
            || value.TechnicianProfileIds.Any(i=>i<1) || value.ServiceLogins.Any(s=>string.IsNullOrWhiteSpace(s)||s.Length>255))
            throw new ArgumentException("Проверьте настройки организации отчётности GLPI");
        return value;
    }

    public bool IsService(int id, string login) => ServiceUserIds.Contains(id)
        || ServiceLogins.Contains(login, StringComparer.OrdinalIgnoreCase);
    public static object Presentation() => new {timeZone=Current.TimeZoneId,locale=Current.Locale,organizationName=Current.OrganizationName};
    public bool EquivalentTo(ReportDeployment other) => TimeZoneId==other.TimeZoneId && Locale==other.Locale
        && OrganizationName==other.OrganizationName && ServiceUserIds.SequenceEqual(other.ServiceUserIds)
        && ServiceLogins.SequenceEqual(other.ServiceLogins) && TechnicianProfileIds.SequenceEqual(other.TechnicianProfileIds);
}
