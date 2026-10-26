using System.Globalization;
using System.Text.Json;

namespace LitleHelperServer;

public partial class GlpiService
{
    private readonly HashSet<int> reportServiceIds = [];
    public bool ReportTechnician(ReportActor a) => !a.IsService && !reportServiceIds.Contains(a.Id) && ReportMath.IsTechnician(a);
    private async Task<JsonElement[]> ReportAll(string type, string session, CancellationToken ct)
    {
        var result = new List<JsonElement>();
        for (int offset = 0; offset < 100000; offset += 100)
        {
            using var request = Request(HttpMethod.Get, type + $"?range={offset}-{offset + 99}&get_hateoas=false&sort=id&order=ASC", session);
            using var response = await http.SendAsync(request, ct);
            // An empty GLPI collection may return 416 only at an exhausted offset.
            if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable && offset > 0) break;
            await EnsureSuccess(response, ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (json.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("GLPI reports: expected a complete collection for " + type);
            var page = json.RootElement.EnumerateArray().Select(x => x.Clone()).ToArray();
            result.AddRange(page);
            if (page.Length < 100) return result.ToArray();
            if (result.Count >= 100000) throw new InvalidOperationException("GLPI reports: collection limit reached; report was not replaced.");
        }
        if (result.Select(x => FieldInt(x, "id")).Distinct().Count() != result.Count) throw new InvalidOperationException("GLPI reports: unstable pagination for " + type);
        return result.ToArray();
    }
    internal static int FieldInt(JsonElement row, string key) => row.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.Number or JsonValueKind.String && int.TryParse(v.ToString(), out var n) ? n : 0;
    internal static DateTime? FieldDate(JsonElement row, string key, TimeZoneInfo zone) => row.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? ReportMath.ParseSourceDate(v.GetString(), zone) : null;

    public async Task<ReportTicket[]> ReportSnapshotAsync(Dictionary<int, int> locationBranches, Dictionary<int, string> branches, CancellationToken ct)
    {
        string session = await Init(ct);
        try
        {
            // GLPI 11 changes the SQL session timezone to the API user's preference.
            // The resulting timestamp strings carry NO offset. Read it from that very
            // session instead of treating wall clock strings as UTC or using our UI zone.
            var context = await ReadInSessionAsync("getFullSession", session, ct);
            if (!context.TryGetProperty("session", out var fullSession)) throw new InvalidOperationException("GLPI did not return its API session timezone");
            reportServiceIds.Clear();
            reportServiceIds.Add(FieldInt(fullSession, "glpiID"));
            reportServiceIds.Add(settings.ServiceUserId);
            reportServiceIds.UnionWith(ReportDeployment.Current.ServiceUserIds);
            string timezone = Text(fullSession, "glpitimezone", "0");
            var sourceZone = TimeZoneInfo.FindSystemTimeZoneById(timezone is "0" or "" ? store.ReportingTimeZone : timezone);
            var tickets = await ReportAll("Ticket", session, ct);
            var users = (await ReportAll("User", session, ct)).ToDictionary(x => FieldInt(x, "id"));
            var actors = await ReportAll("Ticket_User", session, ct);
            var profiles = (await ReportAll("Profile", session, ct)).ToDictionary(x => FieldInt(x, "id"), x => Text(x, "name"));
            var profileUsers = await ReportAll("Profile_User", session, ct);
            var solutions = await ReportAll("ITILSolution", session, ct);
            var tasks = await ReportAll("TicketTask", session, ct);
            var surveys = await ReportAll("TicketSatisfaction", session, ct);
            var categories = (await ReportAll("ITILCategory", session, ct)).ToDictionary(x => FieldInt(x, "id"), x => Text(x, "completename", Text(x, "name")));
            var locations = (await ReportAll("Location", session, ct)).ToDictionary(x => FieldInt(x, "id"));
            ReportActor Actor(int id, int entity)
            {
                users.TryGetValue(id, out var user);
                string name = user.ValueKind == JsonValueKind.Undefined ? "GLPI #" + id : (Text(user, "realname") + " " + Text(user, "firstname")).Trim();
                string login = user.ValueKind == JsonValueKind.Undefined ? "" : Text(user, "name");
                var ownProfiles = profileUsers.Where(p => FieldInt(p, "users_id") == id && (FieldInt(p, "entities_id") == entity || FieldInt(p, "entities_id") == 0 && FieldInt(p, "is_recursive") == 1))
                    .Select(p => profiles.GetValueOrDefault(FieldInt(p, "profiles_id"), "")).Distinct().ToArray();
                bool? technicianProfile=ReportDeployment.Current.TechnicianProfileIds.Length==0 ? null
                    : profileUsers.Any(p=>FieldInt(p,"users_id")==id
                        && (FieldInt(p,"entities_id")==entity || FieldInt(p,"entities_id")==0 && FieldInt(p,"is_recursive")==1)
                        && ReportDeployment.Current.TechnicianProfileIds.Contains(FieldInt(p,"profiles_id")));
                return new(id, login, name.Length == 0 ? login : name, string.Join(", ", ownProfiles),
                    reportServiceIds.Contains(id) || ReportDeployment.Current.IsService(id,login),technicianProfile);
            }
            bool Qualified(ReportActor a) => ReportTechnician(a);
            string web = settings.BaseUrl[..settings.BaseUrl.LastIndexOf("/apirest.php", StringComparison.OrdinalIgnoreCase)];
            var rows = new List<ReportTicket>();
            foreach (var t in tickets)
            {
                int id = FieldInt(t, "id"), entity = FieldInt(t, "entities_id");
                var links = actors.Where(a => FieldInt(a, "tickets_id") == id).OrderBy(a => FieldInt(a, "id")).ToArray();
                ReportActor[] Participants(int type) => links.Where(a => FieldInt(a, "type") == type).Select(a => Actor(FieldInt(a, "users_id"), entity)).Where(a => a.Id > 0).DistinctBy(a => a.Id).ToArray();
                var assigned = Participants(2); var requesters = Participants(1);
                var solution = solutions.Where(s => Text(s, "itemtype") == "Ticket" && FieldInt(s, "items_id") == id && FieldInt(s, "status") != 3).OrderByDescending(s => FieldDate(s, "date_creation", sourceZone)).ThenByDescending(s => FieldInt(s, "id")).FirstOrDefault();
                ReportActor? responsible = null; string attribution = "Не определён";
                if (solution.ValueKind != JsonValueKind.Undefined && FieldInt(solution, "users_id") > 0)
                {
                    var author = Actor(FieldInt(solution, "users_id"), entity);
                    if (Qualified(author)) { responsible = author; attribution = "Автор последнего неотклонённого решения; восстановлено по данным GLPI"; }
                }
                var specialists = assigned.Where(Qualified).ToArray();
                if (responsible == null && specialists.Length == 1) { responsible = specialists[0]; attribution = "Единственный назначенный специалист; сервисные записи исключены"; }
                int loc = FieldInt(t, "locations_id"); int? branchId = null; var seen = new HashSet<int>();
                while (loc > 0 && seen.Add(loc)) { if (locationBranches.TryGetValue(loc, out int b)) { branchId = b; break; } loc = locations.TryGetValue(loc, out var parent) ? FieldInt(parent, "locations_id") : 0; }
                var survey = surveys.FirstOrDefault(s => FieldInt(s, "tickets_id") == id);
                DateTime? answer = survey.ValueKind == JsonValueKind.Undefined ? null : FieldDate(survey, "date_answered", sourceZone);
                double? score = null;
                if (answer != null && survey.TryGetProperty("satisfaction_scaled_to_5", out var scaled) && double.TryParse(scaled.ToString(), CultureInfo.InvariantCulture, out double sc) && sc is >= 0 and <= 5) score = sc;
                int effort = tasks.Where(task => FieldInt(task, "tickets_id") == id).Sum(task => Math.Max(0, FieldInt(task, "actiontime")));
                if (effort == 0) effort = FieldInt(t, "actiontime");
                rows.Add(new() { Id = id, Title = GlpiTicketImport.Plain(Text(t, "name")), Url = web + "/front/ticket.form.php?id=" + id, EntityId = entity,
                    BranchId = branchId, Branch = branchId.HasValue ? branches.GetValueOrDefault(branchId.Value, "Не сопоставлен") : "Не сопоставлен",
                    Category = categories.GetValueOrDefault(FieldInt(t, "itilcategories_id"), "Не указана"), Priority = FieldInt(t, "priority"), Status = FieldInt(t, "status"), Deleted = FieldInt(t, "is_deleted") != 0,
                    CreatedAt = FieldDate(t, "date", sourceZone), AcceptedAt = FieldDate(t, "takeintoaccountdate", sourceZone), SolvedAt = FieldDate(t, "solvedate", sourceZone), ClosedAt = FieldDate(t, "closedate", sourceZone),
                    WaitingSeconds = FieldInt(t, "waiting_duration"), EffortSeconds = effort > 0 ? effort : null, Requesters = requesters, Assignees = assigned,
                    RegisteredBy = FieldInt(t, "users_id_recipient") > 0 ? Actor(FieldInt(t, "users_id_recipient"), entity) : null, Responsible = responsible, Attribution = attribution,
                    Score = score, AnsweredAt = answer });
            }
            if (rows.Select(t => t.Id).Distinct().Count() != rows.Count) throw new InvalidOperationException("GLPI reports: duplicate ticket IDs");
            return rows.ToArray();
        }
        finally { await Kill(session); }
    }
}
