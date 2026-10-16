using System.Windows;

namespace PixelHelper;

public sealed partial class PetWindow
{
    private TicketSurveyNotice[] pendingSurveys = [];
    private TicketSurveyWindow? surveyWindow;
    private bool openingSurvey;
    private void InitializeSurveys()
    {
        hub!.VerifySurveyUser = BindVerifiedAgentAsync;
        hub.SurveysUpdated += rows => Dispatcher.BeginInvoke(new Action(async () => {
            pendingSurveys=rows;
            if(surveyWindow!=null && !surveyWindow.Busy && !rows.Any(i=>i.Id==surveyWindow.SurveyId && i.State=="Pending")) surveyWindow.CloseFromServer();
            if(menuOpen && !announcementVisible) ShowMenu();
            var due=rows.FirstOrDefault(i=>i.State=="Pending" && i.NextNoticeAt<=DateTime.UtcNow);
            if(due!=null && !openingSurvey && surveyWindow==null) await ShowSurvey(due);
        }));
        hub.SurveyChanged += id => Dispatcher.BeginInvoke(new Action(() => { if(surveyWindow?.SurveyId==id && !surveyWindow.Busy)surveyWindow.CloseFromServer(); }));
        hub.BeforeTicket = async () => {
            var rows=await hub.GetSurveys();var required=rows.FirstOrDefault(i=>i.Required);
            if(required==null)return true;
            return await Dispatcher.InvokeAsync(() => {
                surveyWindow?.CloseFromServer();
                var window=new TicketSurveyWindow(hub,required,true);surveyWindow=window;
                window.ShowDialog();surveyWindow=null;
                hub.UrgentTicket=window.UrgentRequested;
                return window.UrgentRequested || window.Submitted;
            });
        };
    }
    private async Task ShowSurvey(TicketSurveyNotice notice)
    {
        if(hub==null || openingSurvey || surveyWindow!=null)return;
        openingSurvey=true;
        try {
            if(notice.State=="Pending" && !await hub.ClaimSurvey(notice.Id))return;
            // Refresh after claiming: the first display starts the deadline on the server.
            var fresh=(await hub.GetSurveys()).FirstOrDefault(i=>i.Id==notice.Id);if(fresh==null)return;
            surveyWindow=new TicketSurveyWindow(hub,fresh);
            surveyWindow.Closed+=(_,_)=>{surveyWindow=null;_ = hub.PollSurveys();};
            surveyWindow.Show();
        } catch(Exception e) { Settings.Log(e); }
        finally { openingSurvey=false; }
    }
    private async Task OpenSurveyQueue()
    {
        if(hub==null)return;
        pendingSurveys=await hub.GetSurveys();
        if(surveyWindow!=null) { surveyWindow.Activate();return; }
        if(pendingSurveys.Length==0) { ShowNotice("Нет заявок, ожидающих вашей оценки");return; }
        await ShowSurvey(pendingSurveys[0]);
    }
}
