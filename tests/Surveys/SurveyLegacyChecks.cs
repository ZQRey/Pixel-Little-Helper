using System.Text.Json;
using LitleHelperServer;
using Microsoft.EntityFrameworkCore;

internal static class SurveyLegacyChecks
{
    public static async Task Run(HelperDb db,GlpiReports reports,TicketSurveys surveys,Action<bool,string> check)
    {
        var state=await reports.State(default);string oldPolicy=state.SurveyPolicyJson;
        var i=await db.TicketSurveyInvitations.SingleAsync(x=>x.TicketId==901);
        string original=JsonSerializer.Serialize(i);
        var user=new PanelUser {Id=4201,Username="alice",PasswordHash="!AD",IsActive=true};
        var pc=new Computer {MachineName="SURVEY-LEGACY-FIXTURE",CurrentUser="alice",ConnectionId="survey-legacy-fixture",IsOnline=true};
        db.Users.Add(user);db.Computers.Add(pc);
        state.SurveyPolicyJson=JsonSerializer.Serialize(new SurveyPolicy());
        i.State="Pending";i.Mandatory=false;i.NextNoticeAt=DateTime.UtcNow.AddMinutes(-1);i.LastSentAt=null;i.FirstShownAt=null;i.Notices=0;i.ExpiresAt=DateTime.UtcNow.AddDays(1);
        await db.SaveChangesAsync();AgentUserSessions.BindVerified(pc.ConnectionId,user);
        await surveys.Notify(default);
        check(i.Notices==1 && i.NextNoticeAt>DateTime.UtcNow && i.FirstShownAt==null,"legacy MSI delivery attempt is limited without claiming form was shown");
        await surveys.Notify(default);
        check(i.Notices==1,"voluntary reminder cannot repeat immediately on legacy MSI");
        i.Notices=3;i.NextNoticeAt=DateTime.UtcNow.AddDays(-1);i.LastSentAt=DateTime.UtcNow.AddDays(-2);await db.SaveChangesAsync();
        await surveys.Notify(default);
        check(i.Notices==3,"legacy voluntary notifications stop after three attempts");
        i.Notices=0;i.LastSentAt=null;pc.IsOnline=false;await db.SaveChangesAsync();await surveys.Notify(default);
        check(i.Notices==0 && i.LastSentAt==null,"offline requester does not consume notification attempts");
        AgentUserSessions.Remove(pc.ConnectionId);
        db.Entry(i).CurrentValues.SetValues(JsonSerializer.Deserialize<TicketSurveyInvitation>(original)!);
        state.SurveyPolicyJson=oldPolicy;db.Computers.Remove(pc);db.Users.Remove(user);await db.SaveChangesAsync();
    }
}
