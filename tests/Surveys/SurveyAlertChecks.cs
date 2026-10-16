using System.Text.Json;
using LitleHelperServer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

internal static class SurveyAlertChecks
{
    public static async Task Run(HelperDb db,GlpiReports reports,ReportGate gate,IHubContext<HelperHub> hub,
        Action<bool,string> check,Func<Func<Task>,string,Task> reject)
    {
        var state=await reports.State(default);
        string oldPayload=state.Payload,oldPolicy=state.SurveyPolicyJson;
        bool oldEnabled=state.SurveysEnabled;
        var now=new DateTime(2026,10,19,10,0,0,DateTimeKind.Utc);
        var clock=new FixtureClock(now);
        var admin=new PanelUser {Id=4101,Username="alert-admin",Role=Roles.SuperAdmin,IsActive=true};
        var scoped=new PanelUser {Id=4102,Username="alert-scoped",PasswordHash="!AD",BranchId=10,IsActive=true,
            Permissions=new(){["reports.glpi.view"]=true,["reports.glpi.settings"]=true}};
        var ordinary=new PanelUser {Id=4103,Username="alert-ordinary",IsActive=true};
        db.Users.AddRange(admin,scoped,ordinary);
        var policy=new SurveyPolicy {Mandatory=true,AdminUserIds=[4101,4102],UndeliveredBusinessHours=1};
        state.SurveyPolicyJson=JsonSerializer.Serialize(policy);state.SurveysEnabled=true;
        ReportTicket Ticket(int id,int branch)=>new(){Id=id,Status=6,BranchId=branch,Title="Alert fixture",ClosedAt=now.AddDays(-7),
            Requesters=[new(7,"alice","Alice")],Responsible=new(8,"tech","Tech")};
        var tickets=new[]{Ticket(99001,10),Ticket(99002,10),Ticket(99003,20)};
        state.Payload=JsonSerializer.Serialize(tickets);
        TicketSurveyInvitation Invite(ReportTicket t)=>new(){Id=Guid.NewGuid().ToString("N"),Source=reports.Source,TicketId=t.Id,
            Owner="alice",RequesterId=7,ClosedAt=t.ClosedAt!.Value,ExpiresAt=now.AddDays(7),Mandatory=true};
        var missing=Invite(tickets[0]);
        var overdue=Invite(tickets[1]);overdue.FirstShownAt=now.AddHours(-2);overdue.DueAt=now.AddHours(-1);
        var blocked=Invite(tickets[2]);blocked.State="Blocked";
        db.TicketSurveyInvitations.AddRange(missing,overdue,blocked);await db.SaveChangesAsync();
        var alerts=new SurveyAlerts(db,reports,gate,hub,clock);
        var first=(await alerts.Current(admin,default))!;
        check(first.Unread && first.Attention.Total==3 && first.Attention.Undelivered==1 && first.Attention.Overdue==1 && first.Attention.Blocked==1,"one durable admin digest combines problems without double counting");
        check(first.Attention.Unmapped==1,"missing AD account is flagged after delivery grace period");
        check((await alerts.Current(scoped,default))!.Attention.TicketIds.SequenceEqual(new[]{99001,99002}),"scoped administrator receives only allowed branch tickets");
        check(await alerts.Current(ordinary,default)==null,"ordinary user cannot receive administrative alerts");
        check((await alerts.Current(admin,default))!.Revision==first.Revision,"unchanged digest is deduplicated before repeat interval");
        await alerts.Read(admin,first.Revision,default);
        check(!(await alerts.Current(admin,default))!.Unread,"reading digest survives repeated polls");
        await using(var reopened=new HelperDb(new DbContextOptionsBuilder<HelperDb>().UseSqlite(db.Database.GetConnectionString()).Options)) {
            var saved=await reopened.SurveyAdminAlerts.SingleAsync(a=>a.UserId==admin.Id);
            check(saved.PayloadJson.Contains("99001") && saved.ReadRevision==first.Revision,"offline inbox and read revision persist in database");
        }
        clock.At=policy.AfterBusinessMinutes(now,policy.AdminRepeatMinutes).AddSeconds(1);
        var repeated=(await alerts.Current(admin,default))!;
        check(repeated.Unread && repeated.Revision>first.Revision,"unresolved digest repeats once after configured business interval");
        check((await alerts.Current(admin,default))!.Revision==repeated.Revision,"repeated digest stays deduplicated within interval");
        // The next digest has different problems, even though its total number is unchanged.
        missing.FirstShownAt=now.AddMinutes(-30);missing.DueAt=now.AddMinutes(-1);await db.SaveChangesAsync();
        var changed=(await alerts.Current(admin,default))!;
        check(changed.Revision>first.Revision && changed.Unread && changed.Attention.Total==first.Attention.Total,"new problem classification reopens digest without duplicate inbox rows");
        await reject(()=>alerts.Read(admin,first.Revision,default),"stale acknowledgment cannot hide newer alert");
        scoped.BranchId=20;await db.SaveChangesAsync();
        check((await alerts.Current(scoped,default))!.Attention.TicketIds.SequenceEqual(new[]{99003}),"permission scope changes immediately remove old branch details");
        scoped.Permissions=new(){["reports.glpi.view"]=true};await db.SaveChangesAsync();
        check(await alerts.Current(scoped,default)==null,"revoked settings permission hides persisted alert");
        state.SurveyPolicyJson=JsonSerializer.Serialize(policy with {AdminUserIds=[4102]});await db.SaveChangesAsync();
        check(await alerts.Current(admin,default)==null,"explicit recipient list excludes unselected administrators");
        state.SurveyPolicyJson=JsonSerializer.Serialize(policy with {AdminAlertsEnabled=false});await db.SaveChangesAsync();
        check(await alerts.Current(admin,default)==null,"administrator notifications can be disabled independently");
        state.SurveyPolicyJson=JsonSerializer.Serialize(policy);missing.ExpiresAt=now.AddSeconds(-1);overdue.ExpiresAt=now.AddSeconds(-1);blocked.ExpiresAt=now.AddSeconds(-1);await db.SaveChangesAsync();
        check(await alerts.Current(admin,default)==null,"expired 14-day surveys do not generate actionable reminders");
        missing.ExpiresAt=now.AddDays(1);missing.State="Answered";overdue.State="Cancelled";blocked.State="Exempt";await db.SaveChangesAsync();
        check(await alerts.Current(admin,default)==null,"resolved cancelled and exempt tasks clear the notification");
        await reject(()=>{(policy with {AdminRepeatMinutes=1}).Validate();return Task.CompletedTask;},"admin repeat frequency is validated");
        await reject(()=>{(policy with {AdminUserIds=[0]}).Validate();return Task.CompletedTask;},"invalid administrator recipient ID is rejected");
        state.Payload=oldPayload;state.SurveyPolicyJson=oldPolicy;state.SurveysEnabled=oldEnabled;
        db.TicketSurveyInvitations.RemoveRange(missing,overdue,blocked);db.Users.RemoveRange(admin,scoped,ordinary);
        await db.SaveChangesAsync();
    }
    private sealed class FixtureClock(DateTime at):TimeProvider
    {
        public DateTime At {get;set;}=at;
        public override DateTimeOffset GetUtcNow()=>new(At);
    }
}
