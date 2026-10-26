using Microsoft.AspNetCore.SignalR;

namespace LitleHelperServer;

public partial class HelperHub
{
    private async Task<PanelUser> SurveyUser()
    {
        var computer = await Agent();
        return await AgentUserSessions.UserAsync(computer, db) ?? throw new HubException("Подтвердите доменную учётную запись через мессенджер помощника");
    }
    private TicketSurveys SurveyService => Context.GetHttpContext()!.RequestServices.GetRequiredService<TicketSurveys>();
    public async Task<TicketSurveyNotice[]> GetTicketSurveys()
    {
        var computer = await Agent(); var user = await AgentUserSessions.UserAsync(computer, db);
        SurveyClients.Register(Context.ConnectionId,queue:false);
        return user == null ? [] : await SurveyService.Pending(user, Context.ConnectionAborted, true);
    }
    public async Task AcknowledgeTicketSurvey(string id) => await SurveyService.Acknowledge(await SurveyUser(), id, Context.ConnectionAborted);
    public async Task<bool> ClaimTicketSurvey(string id)
    {
        bool claimed=await SurveyService.Claim(await SurveyUser(),id,Context.ConnectionId,Context.ConnectionAborted);
        Context.GetHttpContext()!.RequestServices.GetRequiredService<ILogger<HelperHub>>()
            .LogInformation("Survey native claim {SurveyId} on {Machine}: {Claimed}",id,Context.User?.Identity?.Name,claimed);
        return claimed;
    }
    public async Task<TicketSurveyNotice[]> GetAllTicketSurveys()
    {
        var computer=await Agent();var user=await AgentUserSessions.UserAsync(computer,db);
        bool known = SurveyClients.SupportsQueue(Context.ConnectionId);
        SurveyClients.Register(Context.ConnectionId);
        if (!known) {
            Context.GetHttpContext()!.RequestServices.GetRequiredService<ILogger<HelperHub>>()
                .LogInformation("Survey queue registered on {Machine}; verified user {UserId}",computer.MachineName,user?.Id);
            await Clients.Caller.SendAsync("OnButtonsUpdated", await Buttons(db,computer));
        }
        return user==null ? [] : await SurveyService.Pending(user,Context.ConnectionAborted);
    }
    public async Task<object> AnswerTicketSurvey(string id, TicketSurveyAnswer answer) => await SurveyService.Answer(await SurveyUser(), id, answer, Context.ConnectionAborted);
    public async Task DeferTicketSurvey(string id, bool dismiss) => await SurveyService.Defer(await SurveyUser(), id, dismiss, Context.ConnectionAborted);
    public async Task SnoozeTicketSurvey(string id, int minutes) => await SurveyService.Defer(await SurveyUser(), id, false, Context.ConnectionAborted,minutes);
}
