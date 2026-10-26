using System.Text.Json;

namespace LitleHelperServer;

public record ReportOrganizationUpdate(string Name, string TimeZone, string Locale,
    int[] ServiceUserIds, string[] ServiceLogins, int[] TechnicianProfileIds);

public sealed class ReportOrganizationStore(IConfiguration configuration)
{
    private readonly object gate = new();
    private string FilePath => Path.GetFullPath(configuration["GlpiReports:OrganizationFile"] ?? "data/report-organization.json");
    public object View()
    {
        lock(gate) {
            var effective = ReportDeployment.Current;
            var saved = File.Exists(FilePath) ? ReportDeployment.Parse(new ConfigurationBuilder().AddJsonFile(FilePath).Build()) : effective;
            return new { current=effective, pending=saved, restartRequired=!saved.EquivalentTo(effective) };
        }
    }
    public void Save(ReportOrganizationUpdate input)
    {
        if(input.Name==null||input.TimeZone==null||input.Locale==null||input.ServiceUserIds==null||input.ServiceLogins==null||input.TechnicianProfileIds==null
            ||input.ServiceUserIds.Length>1000||input.TechnicianProfileIds.Length>1000||input.ServiceLogins.Length>1000)
            throw new ArgumentException("Проверьте параметры организации");
        var payload = new { GlpiReports=new { Organization=input } };
        string json=JsonSerializer.Serialize(payload);
        using var stream=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        _=ReportDeployment.Parse(new ConfigurationBuilder().AddJsonStream(stream).Build());
        lock(gate) {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temp=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                File.WriteAllText(temp,json);
                if(!OperatingSystem.IsWindows())File.SetUnixFileMode(temp,UnixFileMode.UserRead|UnixFileMode.UserWrite);
                File.Move(temp,FilePath,true);
            } finally { if(File.Exists(temp))File.Delete(temp); }
        }
    }
}
