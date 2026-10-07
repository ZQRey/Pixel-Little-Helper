using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
namespace LitleHelperServer;
public class ChatPreference
{
    public string Id { get; set; }=""; public int UserId { get; set; } public int Peer { get; set; }
    public bool Pinned { get; set; } public bool Favourite { get; set; } public bool Muted { get; set; }
}
public record PreferenceUpdate(bool Pinned,bool Favourite,bool Muted);
public class ChatReaction
{
    public string Id { get; set; }=""; public long MessageId { get; set; } public bool IsGroup { get; set; }
    public int UserId { get; set; } public string Emoji { get; set; }="";
}
public record ReactionUpdate(string Emoji);
public record ReactionInfo(string Emoji,int Count,bool Mine);
public static class ChatExtras
{
    public static async Task EnsureSchemaAsync(HelperDb db)
    {
        string boolean=db.Database.IsNpgsql()?"boolean":"INTEGER";
        await db.Database.ExecuteSqlRawAsync($"CREATE TABLE IF NOT EXISTS \"ChatPreferences\" (\"Id\" TEXT PRIMARY KEY,\"UserId\" INTEGER NOT NULL,\"Peer\" INTEGER NOT NULL,\"Pinned\" {boolean} NOT NULL,\"Favourite\" {boolean} NOT NULL,\"Muted\" {boolean} NOT NULL)");
        await db.Database.ExecuteSqlRawAsync($"CREATE TABLE IF NOT EXISTS \"ChatReactions\" (\"Id\" TEXT PRIMARY KEY,\"MessageId\" BIGINT NOT NULL,\"IsGroup\" {boolean} NOT NULL,\"UserId\" INTEGER NOT NULL,\"Emoji\" TEXT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_ChatReactions_Message\" ON \"ChatReactions\" (\"IsGroup\",\"MessageId\")");
    }
    public static async Task ValidatePeer(HelperDb db,int me,int peer)
    {
        if(peer==0||peer==int.MinValue||peer==me)throw new ArgumentException("Выберите диалог.");
        if(peer<0)await ChatGroups.Member(db,-peer,me);
        else if(!await db.Users.AnyAsync(u=>u.Id==peer&&u.IsActive&&u.PasswordHash=="!AD"))throw new ArgumentException("Получатель недоступен.");
    }
    public static async Task<bool> VisibleMessage(HelperDb db,int me,int peer,long id)
    {
        if(peer<0){var member=await ChatGroups.Member(db,ChatGroups.IdFromPeer(peer),me);return await ChatGroups.Visible(db,member).AnyAsync(m=>m.Id==id);}
        return await db.ChatMessages.AnyAsync(m=>m.Id==id&&(m.SenderId==me&&m.RecipientId==peer||m.SenderId==peer&&m.RecipientId==me));
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/preferences",async(HelperDb db,ClaimsPrincipal p)=>{var me=await Messenger.UserAsync(db,p);return await db.ChatPreferences.Where(x=>x.UserId==me.Id).ToListAsync();});
        api.MapPost("/preferences/{peer:int}",async(int peer,PreferenceUpdate update,HelperDb db,ClaimsPrincipal p,IHubContext<MessengerHub> hub,ChatGroupGate gate)=>
        {
            var me=await Messenger.UserAsync(db,p);await ValidatePeer(db,me.Id,peer);await gate.Semaphore.WaitAsync();
            try {string id=me.Id+":"+peer;var pref=await db.ChatPreferences.FindAsync(id);if(pref==null){pref=new(){Id=id,UserId=me.Id,Peer=peer};db.ChatPreferences.Add(pref);}pref.Pinned=update.Pinned;pref.Favourite=update.Favourite;pref.Muted=update.Muted;await db.SaveChangesAsync();await hub.Clients.Group("Chat:"+me.Id).SendAsync("ChatChanged");return Results.Ok(pref);}finally{gate.Semaphore.Release();}
        });
        api.MapGet("/extras/{peer:int}",async(int peer,string? ids,HelperDb db,ClaimsPrincipal p)=>
        {
            var me=await Messenger.UserAsync(db,p);if(peer<0)await ChatGroups.Member(db,ChatGroups.IdFromPeer(peer),me.Id);
            var requested=(ids??"").Split(',').Take(100).Select(s=>long.TryParse(s,out var value)?value:0).Where(v=>v>0).Distinct().ToArray();List<long> allowed;if(peer<0){var member=await ChatGroups.Member(db,ChatGroups.IdFromPeer(peer),me.Id);allowed=await ChatGroups.Visible(db,member).Where(m=>requested.Contains(m.Id)).Select(m=>m.Id).ToListAsync();}else allowed=await db.ChatMessages.Where(m=>requested.Contains(m.Id)&&(m.SenderId==me.Id&&m.RecipientId==peer||m.SenderId==peer&&m.RecipientId==me.Id)).Select(m=>m.Id).ToListAsync();
            var reactions=await db.ChatReactions.Where(r=>r.IsGroup==(peer<0)&&allowed.Contains(r.MessageId)).ToListAsync();return allowed.ToDictionary(id=>id.ToString(),id=>reactions.Where(r=>r.MessageId==id).GroupBy(r=>r.Emoji).Select(g=>new ReactionInfo(g.Key,g.Count(),g.Any(r=>r.UserId==me.Id))));
        });
        api.MapPost("/reaction/{peer:int}/{message:long}",async(int peer,long message,ReactionUpdate update,HelperDb db,ClaimsPrincipal p,IHubContext<MessengerHub> hub,ChatGroupGate gate)=>
        {
            var me=await Messenger.UserAsync(db,p);if(!new[]{"👍","❤️","😊","🎉","😔","👋"}.Contains(update.Emoji))throw new ArgumentException("Неизвестная реакция.");if(!await VisibleMessage(db,me.Id,peer,message))throw new UnauthorizedAccessException();await gate.Semaphore.WaitAsync();
            try{string key=(peer<0?"g":"p")+":"+message+":"+me.Id;var old=await db.ChatReactions.FindAsync(key);if(old!=null){if(old.Emoji==update.Emoji)db.ChatReactions.Remove(old);else old.Emoji=update.Emoji;}else db.ChatReactions.Add(new(){Id=key,MessageId=message,IsGroup=peer<0,UserId=me.Id,Emoji=update.Emoji});await db.SaveChangesAsync();
                var users=peer<0?await db.ChatGroupMembers.Where(m=>m.GroupId==-peer).Select(m=>m.UserId).ToListAsync():new List<int>{me.Id,peer};await hub.Clients.Groups(users.Select(u=>"Chat:"+u)).SendAsync("ChatChanged");return Results.Ok(new {success=true});
            }finally{gate.Semaphore.Release();}
        });
        api.MapGet("/search/{peer:int}",async(int peer,string q,long? before,HelperDb db,ClaimsPrincipal p)=>
        {
            var me=await Messenger.UserAsync(db,p);q=q.Trim();if(q.Length is <2 or >200)throw new ArgumentException("Введите от 2 до 200 символов.");
            if(peer<0){var member=await ChatGroups.Member(db,ChatGroups.IdFromPeer(peer),me.Id);return Results.Ok(await ChatGroups.Visible(db,member).Where(m=>(before==null||m.Id<before)&&m.Body.ToLower().Contains(q.ToLower())).OrderByDescending(m=>m.Id).Take(50).Select(m=>new{m.Id,m.Body,m.SentAt}).ToListAsync());}
            return Results.Ok(await db.ChatMessages.AsNoTracking().Where(m=>(m.SenderId==me.Id&&m.RecipientId==peer||m.SenderId==peer&&m.RecipientId==me.Id)&&(before==null||m.Id<before)&&m.Body.ToLower().Contains(q.ToLower())).OrderByDescending(m=>m.Id).Take(50).Select(m=>new{m.Id,m.Body,m.SentAt}).ToListAsync());
        });
    }
}
