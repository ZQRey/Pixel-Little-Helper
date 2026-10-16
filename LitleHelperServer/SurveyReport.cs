namespace LitleHelperServer;

public sealed record SurveyReportRow(string Id,int TicketId,string Title,string Requester,string Specialist,string State,
    bool Mandatory,DateTime ClosedAt,DateTime ExpiresAt,DateTime? FirstShownAt,DateTime? DueAt,DateTime? AnsweredAt,string Error);
public sealed record SurveyReportData(int Eligible,int Answered,int Pending,int Overdue,int Undelivered,int Writing,int Blocked,int Expired,int Exempt,
    double? ResponsePercent,double? MeanAnswerSeconds,SurveyReportRow[] Rows)
{
    public static SurveyReportData Build(IEnumerable<TicketSurveyInvitation> invitations, ReportTicket[] tickets,DateTime now)
    {
        var byId=tickets.ToDictionary(t=>t.Id);
        var rows=invitations.Where(i=>byId.ContainsKey(i.TicketId)).Select(i=>new SurveyReportRow(i.Id,i.TicketId,byId[i.TicketId].Title,i.Owner,i.ResponsibleName,
            SurveyPolicy.Active(i) && i.ExpiresAt<=now?"Expired":i.State,i.Mandatory,i.ClosedAt,i.ExpiresAt,i.FirstShownAt,i.DueAt,i.AnsweredAt,
            SurveyPolicy.Active(i) && i.ExpiresAt<=now?"Истекли 14 дней после закрытия заявки":i.LastError)).ToArray();
        var cohort=rows.Where(i=>i.Mandatory && i.State is not ("Cancelled" or "Exempt")).ToArray();
        int answered=cohort.Count(i=>i.State=="Answered");
        var times=cohort.Where(i=>i.AnsweredAt!=null && i.AnsweredAt>=i.ClosedAt).Select(i=>(i.AnsweredAt!.Value-i.ClosedAt).TotalSeconds).ToArray();
        return new(cohort.Length,answered,cohort.Count(i=>i.State=="Pending"),cohort.Count(i=>i.State=="Pending" && i.DueAt<=now),
            cohort.Count(i=>i.State=="Pending" && i.FirstShownAt==null),cohort.Count(i=>i.State=="Writing"),cohort.Count(i=>i.State=="Blocked"),
            cohort.Count(i=>i.State=="Expired"),rows.Count(i=>i.Mandatory && i.State=="Exempt"),cohort.Length==0?null:100d*answered/cohort.Length,
            times.Length==0?null:times.Average(),rows.OrderBy(i=>i.ClosedAt).ToArray());
    }
}
