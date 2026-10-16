using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
namespace LitleHelperServer;
public static class AgentUserSessions
{
    private sealed record Binding(int UserId,int Version,string Username,DateTime Until);
    private static readonly ConcurrentDictionary<string,Binding> bindings=new();
    public static void Remove(string connection) => bindings.TryRemove(connection,out _);
    public static async Task<PanelUser?> UserAsync(Computer computer,HelperDb db) {
        if(computer.ConnectionId==null || !bindings.TryGetValue(computer.ConnectionId,out var binding) || binding.Until<DateTime.UtcNow || !Security.TicketUser(binding.Username).Equals(Security.TicketUser(computer.CurrentUser),StringComparison.OrdinalIgnoreCase))return null;
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u=>u.Id==binding.UserId && u.IsActive && u.SecurityVersion==binding.Version);
    }
    public static bool IsVerified(Computer computer) => computer.ConnectionId!=null && bindings.TryGetValue(computer.ConnectionId,out var binding) && binding.Until>DateTime.UtcNow && Security.TicketUser(binding.Username).Equals(Security.TicketUser(computer.CurrentUser),StringComparison.OrdinalIgnoreCase);
    internal static void BindVerified(string connection,PanelUser user)=>bindings[connection]=new(user.Id,user.SecurityVersion,user.Username,DateTime.UtcNow.AddMinutes(30));
    public record BindRequest(string MachineName,string ClientKey,string ConnectionId);
    public static void MapAgentUserSessions(this WebApplication app) {
        app.MapPost("/api/messenger/bind-agent",async(BindRequest request,ClaimsPrincipal principal,HelperDb db,IHubContext<HelperHub> hub)=> {
            if(request.ClientKey.Length>256 || request.ConnectionId.Length>256) return Results.BadRequest();
            var machine=await db.Computers.SingleOrDefaultAsync(c=>c.MachineName==Security.Canonical(request.MachineName) && c.ConnectionId==request.ConnectionId && c.IsOnline);
            if(machine==null || string.IsNullOrEmpty(machine.AgentKeyHash) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(machine.AgentKeyHash),Encoding.ASCII.GetBytes(Security.KeyHash(request.ClientKey)))) return Results.Unauthorized();
            var user=await db.Users.SingleAsync(u=>u.Username==principal.Identity!.Name);
            if(!user.IsActive || user.MustChangePassword)return Results.Unauthorized();
            string old=Security.TicketUser(machine.CurrentUser).ToLowerInvariant();
            await hub.Groups.RemoveFromGroupAsync(request.ConnectionId,"AgentUser:"+old);
            machine.CurrentUser=user.Username;machine.CurrentUserFullName=user.FullName;
            BindVerified(request.ConnectionId,user);
            await db.SaveChangesAsync();
            await hub.Groups.AddToGroupAsync(request.ConnectionId,"AgentUser:"+Security.TicketUser(user.Username).ToLowerInvariant());
            EmergencyDirectorySync.Request();
            return Results.Ok(new {success=true});
        }).RequireAuthorization("Panel").RequireRateLimiting("registration");
    }
}
