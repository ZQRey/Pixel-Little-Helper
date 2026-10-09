using Microsoft.EntityFrameworkCore;
namespace LitleHelperServer;
public record ChatCommandText(string Body, bool Urgent, string? Command);
public static class ChatCommands
{
    public static ChatCommandText Parse(string input)
    {
        string body=input.Trim(); bool urgent=false; string? command=null;
        while(body.StartsWith('/')) {
            int end=0; while(end<body.Length&&!char.IsWhiteSpace(body[end]))end++;
            string verb=body[..end].ToLowerInvariant();
            if(verb=="/срочно")urgent=true;
            else if(verb=="/танец")command="dance";
            else if(verb=="/pet"||verb=="/погладить"||verb=="/питомец")command="pet";
            else throw new ArgumentException("Неизвестная команда. Откройте помощь в настройках мессенджера.");
            body=body[end..].TrimStart();
        }
        if(body.Length==0&&urgent&&command==null)throw new ArgumentException("После /срочно введите текст сообщения.");
        return new(body,urgent,command);
    }
    public static async Task<List<ChatMessage>> PendingAsync(HelperDb db,int user)
    {
        var since=DateTime.UtcNow.AddHours(-1);
        var result=await db.ChatMessages.AsNoTracking().Where(m=>m.RecipientId==user&&m.SentAt>since&&(m.Command!=null||m.IsUrgent&&m.AcknowledgedAt==null)).OrderBy(m=>m.Id).Take(100).ToListAsync();
        foreach(var message in result) if(message.AcknowledgedAt != null) message.IsUrgent=false;
        foreach(var member in await db.ChatGroupMembers.Where(m=>m.UserId==user).ToListAsync()) {
            if(!await db.ChatGroups.AnyAsync(g=>g.Id==member.GroupId&&!g.IsDeleted)||await db.ChatGroupRestrictions.AnyAsync(r=>r.GroupId==member.GroupId&&r.UserId==user&&r.EndsAt>DateTime.UtcNow))continue;
            var rows=await ChatGroups.Visible(db,member).Where(m=>m.SenderId!=user&&m.SentAt>since&&(m.Command!=null||m.IsUrgent)).OrderBy(m=>m.Id).Take(100).ToListAsync();
            result.AddRange(rows.Select(m=>new ChatMessage { Id=m.Id, SenderId=m.SenderId, RecipientId=-m.GroupId,Body=m.Body,Command=m.Command,IsUrgent=m.IsUrgent,SentAt=m.SentAt,ClientId=m.ClientId }));
        }
        return result.OrderBy(m=>m.SentAt).Take(100).ToList();
    }
}
