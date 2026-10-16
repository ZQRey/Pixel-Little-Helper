using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LitleHelperServer;

public sealed class SurveyAdminAlert
{
    [Key] public string Id { get; set; } = "";
    public string Source { get; set; } = "";
    public int UserId { get; set; }
    public string PayloadJson { get; set; } = "";
    public int Revision { get; set; }
    public int ReadRevision { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime NextNoticeAt { get; set; }
}

public sealed record SurveyAttention(int Overdue,int Undelivered,int Unmapped,int Blocked,int[] TicketIds)
{
    public int Total => TicketIds.Length;
    public static SurveyAttention Build(IEnumerable<TicketSurveyInvitation> invitations,ReportTicket[] tickets,
        HashSet<string> verifiedAccounts,SurveyPolicy policy,DateTime now)
    {
        var visible=tickets.ToDictionary(t=>t.Id);
        var rows=invitations.Where(i=>i.Mandatory && i.ExpiresAt>now && SurveyPolicy.Active(i)
            && visible.TryGetValue(i.TicketId,out var t) && SurveyPolicy.Eligible(i,t)).ToArray();
        var overdue=rows.Where(i=>i.State=="Pending" && i.FirstShownAt!=null && i.DueAt<=now).ToArray();
        var undelivered=rows.Where(i=>i.State=="Pending" && i.FirstShownAt==null
            && policy.AfterBusinessMinutes(i.ClosedAt,policy.UndeliveredBusinessHours*60)<=now).ToArray();
        var unmapped=undelivered.Count(i=>!verifiedAccounts.Contains(Security.NormalizeAccount(i.Owner)));
        var blocked=rows.Where(i=>i.State=="Blocked").ToArray();
        return new(overdue.Length,undelivered.Length,unmapped,blocked.Length,
            overdue.Concat(undelivered).Concat(blocked).Select(i=>i.TicketId).Distinct().Order().ToArray());
    }
}
public sealed record SurveyAlertNotice(int Revision,bool Unread,DateTime UpdatedAt,SurveyAttention Attention);

public sealed class SurveyAlerts(HelperDb db,GlpiReports reports,ReportGate gate,IHubContext<HelperHub> hub,TimeProvider? clock=null)
{
    public static bool Authorized(PanelUser user,SurveyPolicy policy) => user.IsActive && !user.MustChangePassword
        && Access.Can(user,"reports.glpi.view") && Access.Can(user,"reports.glpi.settings")
        && (policy.AdminUserIds.Length==0 || policy.AdminUserIds.Contains(user.Id));
    private async Task<SurveyAlertNotice?> Refresh(PanelUser user,GlpiReportState state,bool push,CancellationToken ct)
    {
        var p=SurveyPolicy.Read(state);
        if(!state.SurveysEnabled || !p.Mandatory || !p.AdminAlertsEnabled || !Authorized(user,p))return null;
        var now=(clock??TimeProvider.System).GetUtcNow().UtcDateTime;
        var accounts=(await db.Users.AsNoTracking().Where(u=>u.IsActive && u.PasswordHash=="!AD" && !u.MustChangePassword).Select(u=>u.Username).ToListAsync(ct))
            .Select(Security.NormalizeAccount).ToHashSet(StringComparer.Ordinal);
        var attention=SurveyAttention.Build(await db.TicketSurveyInvitations.AsNoTracking().Where(i=>i.Source==reports.Source).ToListAsync(ct),
            await reports.Visible(user,state,ct),accounts,p,now);
        var row=await db.SurveyAdminAlerts.SingleOrDefaultAsync(a=>a.Source==reports.Source && a.UserId==user.Id,ct);
        if(attention.Total==0) {
            if(row!=null && row.PayloadJson!="") { row.PayloadJson="";row.ReadRevision=row.Revision;await db.SaveChangesAsync(ct); }
            return null;
        }
        string payload=JsonSerializer.Serialize(attention);
        bool changed=row==null || row.PayloadJson!=payload;
        bool repeat=row!=null && now>=row.NextNoticeAt && p.Working(now);
        if(row==null) {
            row=new(){Id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reports.Source+"\n"+user.Id))),Source=reports.Source,UserId=user.Id};
            db.SurveyAdminAlerts.Add(row);
        }
        if(changed || repeat) {
            row.PayloadJson=payload;row.Revision++;row.UpdatedAt=now;
            row.NextNoticeAt=p.AfterBusinessMinutes(now,p.AdminRepeatMinutes);
            await db.SaveChangesAsync(ct);
            if(push && p.Working(now))await hub.Clients.Group("SurveyAdmin:"+user.Id).SendAsync("SurveyAttentionChanged",ct);
        }
        return new(row.Revision,row.ReadRevision<row.Revision,row.UpdatedAt,attention);
    }
    public async Task<SurveyAlertNotice?> Current(PanelUser user,CancellationToken ct)
    {
        await gate.Alerts.WaitAsync(ct);
        try { return await Refresh(user,await reports.State(ct),false,ct); }
        finally { gate.Alerts.Release(); }
    }
    public async Task Read(PanelUser user,int revision,CancellationToken ct)
    {
        await gate.Alerts.WaitAsync(ct);
        try {
            var state=await reports.State(ct);
            if(!Authorized(user,SurveyPolicy.Read(state)))throw new UnauthorizedAccessException("Уведомление недоступно");
            // Refresh scope before acknowledging: old revisions must not hide newer problems.
            var current=await Refresh(user,state,false,ct);
            if(current==null)return;
            if(revision!=current.Revision)throw new ArgumentException("Сводка изменилась. Обновите уведомление");
            var row=await db.SurveyAdminAlerts.SingleAsync(a=>a.Source==reports.Source && a.UserId==user.Id,ct);
            row.ReadRevision=revision;await db.SaveChangesAsync(ct);
        } finally { gate.Alerts.Release(); }
    }
    public async Task Notify(CancellationToken ct)
    {
        await gate.Alerts.WaitAsync(ct);
        try {
            var state=await reports.State(ct);var p=SurveyPolicy.Read(state);
            if(!state.SurveysEnabled || !p.Mandatory || !p.AdminAlertsEnabled)return;
            foreach(var user in (await db.Users.AsNoTracking().Where(u=>u.IsActive && !u.MustChangePassword).ToListAsync(ct)).Where(u=>Authorized(u,p)))
                await Refresh(user,state,true,ct);
        } finally { gate.Alerts.Release(); }
    }
    public static async Task EnsureSchema(HelperDb db)
    {
        string time=db.Database.IsNpgsql()?"timestamp with time zone":"TEXT";
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS \"SurveyAdminAlerts\" (\"Id\" TEXT PRIMARY KEY, \"Source\" TEXT NOT NULL, \"UserId\" INTEGER NOT NULL, \"PayloadJson\" TEXT NOT NULL, \"Revision\" INTEGER NOT NULL, \"ReadRevision\" INTEGER NOT NULL, \"UpdatedAt\" "+time+" NOT NULL, \"NextNoticeAt\" "+time+" NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_SurveyAdminAlerts_SourceUser\" ON \"SurveyAdminAlerts\" (\"Source\",\"UserId\")");
    }
}
