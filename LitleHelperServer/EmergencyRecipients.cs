using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Novell.Directory.Ldap;
using System.Text.RegularExpressions;
namespace LitleHelperServer;

public static class EmergencyRecipients
{
    private static string Login(string value) => Security.TicketUser(value).ToLowerInvariant();
    private static string Escape(string value) => Regex.Replace(value, @"[\\*()\x00]", m => "\\" + ((int)m.Value[0]).ToString("x2"));
    public static async Task<HashSet<string>> ResolveUsers(HelperDb db, IntegrationSettings settings, IEnumerable<string>? users, IEnumerable<string>? groups, CancellationToken token)
    {
        var targets=new HashSet<string>((users ?? []).Where(s=>!string.IsNullOrWhiteSpace(s)).Select(Login),StringComparer.OrdinalIgnoreCase);
        foreach(var group in groups ?? [])
        {
            if(group.StartsWith("chat:",StringComparison.OrdinalIgnoreCase) && int.TryParse(group[5..],out int chatId))
            {
                var members=await (from member in db.ChatGroupMembers join user in db.Users on member.UserId equals user.Id where member.GroupId==chatId && user.IsActive select user.Username).ToListAsync(token);
                foreach(var username in members) targets.Add(Login(username));
            }
            else if(group.StartsWith("branch:",StringComparison.OrdinalIgnoreCase) && int.TryParse(group[7..],out int branchId))
            {
                foreach(var username in await db.Users.Where(u=>u.IsActive && u.BranchId==branchId).Select(u=>u.Username).ToListAsync(token)) targets.Add(Login(username));
            }
            else if(group.StartsWith("ad:",StringComparison.OrdinalIgnoreCase))
            {
                var options=settings.Ad();
                if(string.IsNullOrWhiteSpace(options.AdminUser)||string.IsNullOrWhiteSpace(options.AdminPassword)) throw new HubException("Для раскрытия групп AD настройте сервисную учётную запись.");
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var ldap=AdAuthentication.Connection(options);
                await ldap.ConnectAsync(options.Host,options.Port,timeout.Token);
                await ldap.BindAsync(options.AdminUser.Contains('@')||options.AdminUser.Contains('\\')?options.AdminUser:options.AdminUser+"@"+options.Domain,options.AdminPassword,timeout.Token);
                var constraints=ldap.SearchConstraints;constraints.ReferralFollowing=false;ldap.Constraints=constraints;
                var result=await ldap.SearchAsync(options.BaseDn,LdapConnection.ScopeSub,"(&(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2))(memberOf:1.2.840.113556.1.4.1941:="+Escape(group[3..])+"))",["sAMAccountName"],false,timeout.Token);
                while(await result.HasMoreAsync(timeout.Token))
                {
                    try {targets.Add(Login((await result.NextAsync(timeout.Token)).Get("sAMAccountName").StringValue));}
                    catch(LdapReferralException) { }
                }
            }
        }
        foreach(var name in await db.Users.Where(u=>!u.IsActive).Select(u=>u.Username).ToListAsync(token)) targets.Remove(Login(name));
        return targets;
    }
    public static async Task<List<Computer>> ResolveTargetMachines(HelperDb db, HashSet<string> users, int? branchId, CancellationToken token)
    {
        var computers=await db.Computers.AsNoTracking().Where(c=>c.IsOnline && c.ConnectionId!=null).ToListAsync(token);
        var branches=await db.Branches.AsNoTracking().Where(b=>b.IsActive).ToListAsync(token);
        return computers.Where(c=>AgentUserSessions.IsVerified(c) && users.Contains(Login(c.CurrentUser)) && (branchId==null || (c.BranchId??Branches.FindByComputerName(branches,c.MachineName,c.IpAddress)?.Id)==branchId)).ToList();
    }
    public static void ValidateScope(EmergencyAlertNotice notice,int? branchId,EmergencyOptions options)
    {
        if (!branchId.HasValue && !options.BroadcastAllBranches && notice.Code is not ("CODE_WHITE" or "CODE_BLUE" or "SPECIALIST_CALL"))
            throw new HubException("Для массового оповещения требуется действующий филиал.");
    }
    public static IEnumerable<string> ResolveSpecialistUsers(EmergencyOptions options, SpecialistDepartmentConfig? department, string code, int? branchId)
    {
        IEnumerable<string> codeBlueUsers = [];
        if (code == "CODE_BLUE")
        {
            if (branchId.HasValue && options.CodeBlueBranchUsers != null && options.CodeBlueBranchUsers.TryGetValue(branchId.Value.ToString(), out var bUsers))
            {
                codeBlueUsers = bUsers;
            }
            else if (branchId.HasValue && options.CodeBlueBranchUsers != null && options.CodeBlueBranchUsers.Count > 0)
            {
                codeBlueUsers = [];
            }
            else
            {
                codeBlueUsers = options.CodeBlueResponsibleUsers ?? [];
            }
        }

        IEnumerable<string> deptUsers = [];
        if (department != null)
        {
            if (branchId.HasValue && department.BranchResponsibleUsers != null && department.BranchResponsibleUsers.TryGetValue(branchId.Value.ToString(), out var bDeptUsers))
            {
                deptUsers = bDeptUsers;
            }
            else if (branchId.HasValue && department.BranchResponsibleUsers != null && department.BranchResponsibleUsers.Count > 0)
            {
                deptUsers = [];
            }
            else
            {
                deptUsers = department.ResponsibleUsers ?? [];
            }
        }

        return codeBlueUsers.Concat(deptUsers);
    }
    public static async Task<List<Computer>> TargetMachines(EmergencyAlertNotice notice,int? branchId,HelperDb db,IntegrationSettings settings,CancellationToken token)
    {
        var options=settings.Emergency();
        var department=options.Departments?.FirstOrDefault(d=>d.Name==notice.Department||d.Id.ToString()==notice.Department);
        if(department==null && notice.Code=="CODE_BLUE")department=options.Departments?.FirstOrDefault(d=>d.Name.Contains("Реаним",StringComparison.OrdinalIgnoreCase)||d.Name.Contains("Синий",StringComparison.OrdinalIgnoreCase));
        var users=ResolveSpecialistUsers(options, department, notice.Code, branchId);
        var groups=(notice.Code=="CODE_BLUE"?options.CodeBlueResponsibleGroups??[]:[]).Concat(department?.ResponsibleGroups??[]);
        return await ResolveTargetMachines(db,await ResolveUsers(db,settings,users,groups,token),branchId,token);
    }
    public static async Task Deliver(EmergencyAlertNotice notice, int? branchId, string? branchName, HelperDb db, IntegrationSettings settings, IHubClients<IClientProxy> clients, CancellationToken token=default)
    {
        var options=settings.Emergency(); var code=notice.Code.ToUpperInvariant();
        if(code=="CODE_WHITE")
        {
            GuardWhiteService.TriggerAlert(notice.Cabinet??"",branchName??"Главный корпус",DateTime.Now.ToString("HH:mm:ss"),notice.EventId);
            await clients.Group("GuardWhite").SendAsync("CodeWhiteAlert",new {cabinet=notice.Cabinet,branch=branchName,time=DateTime.Now.ToString("HH:mm:ss")},token);
            return;
        }
        if(code=="CODE_BLUE" || code=="SPECIALIST_CALL" || !string.IsNullOrWhiteSpace(notice.Department))
        {
            var department=options.Departments?.FirstOrDefault(d=>d.Name.Equals(notice.Department,StringComparison.OrdinalIgnoreCase)||d.Id.ToString()==notice.Department);
            department??=code=="CODE_BLUE"?options.Departments?.FirstOrDefault(d=>d.Name.Contains("Реаним",StringComparison.OrdinalIgnoreCase)||d.Name.Contains("Синий",StringComparison.OrdinalIgnoreCase)):null;
            var users=ResolveSpecialistUsers(options, department, code, branchId);
            var groups=(code=="CODE_BLUE"?options.CodeBlueResponsibleGroups??[]:[]).Concat(department?.ResponsibleGroups??[]);
            var targets=await ResolveUsers(db,settings,users,groups,token);
            if(branchId==null)
            {
                foreach(var username in targets) await clients.Group("AgentUser:"+username).SendAsync("EmergencyAlertNotice",notice,token);
            }
            else
            {
                foreach(var computer in await ResolveTargetMachines(db,targets,branchId,token)) await clients.Group("AgentMachine:"+computer.MachineName.ToLowerInvariant()).SendAsync("EmergencyAlertNotice",notice,token);
            }
            return; // Empty specialist lists never become a mass alert.
        }
        if (!branchId.HasValue && !options.BroadcastAllBranches)
            throw new HubException("Для массового оповещения требуется действующий филиал.");
        await clients.Group(branchId.HasValue && !options.BroadcastAllBranches?"BranchAgents:"+branchId:"AllAgents").SendAsync("EmergencyAlertNotice",notice,token);
    }
    public static void MapEmergencyRecipients(this WebApplication app)
    {
        app.MapGet("/api/settings/emergency/recipients",async(HelperDb db)=>
        {
            var accounts=await db.Users.AsNoTracking().Where(u=>u.IsActive).ToListAsync();
            var computers=await db.Computers.AsNoTracking().Where(c=>c.CurrentUser!="").ToListAsync();
            var users=accounts.Select(u=>new {value=u.Username,label=u.FullName,login=u.Username,room=string.IsNullOrWhiteSpace(u.Room)?computers.FirstOrDefault(c=>Login(c.CurrentUser)==Login(u.Username))?.Room??"":u.Room}).ToList();
            var disabled=(await db.Users.AsNoTracking().Where(u=>!u.IsActive).Select(u=>u.Username).ToListAsync()).Select(Login).ToHashSet();
            users.AddRange(computers.Where(c=>!disabled.Contains(Login(c.CurrentUser)) && !users.Any(u=>Login(u.value)==Login(c.CurrentUser))).GroupBy(c=>Login(c.CurrentUser)).Select(g=>g.First()).Select(c=>new{value=Login(c.CurrentUser),label=c.CurrentUserFullName,login=Login(c.CurrentUser),room=c.Room??""}));

            var groups=await db.ChatGroups.AsNoTracking().Where(g=>!g.IsClosed).Select(g=>new {value="chat:"+g.Id,label=g.Name}).ToListAsync();
            var branches=await db.Branches.AsNoTracking().Where(b=>b.IsActive).Select(b=>new {value="branch:"+b.Id,label=b.Name}).ToListAsync();
            return Results.Ok(new {users,groups=groups.Concat(branches)});
        }).RequireAuthorization("settings.manage");
        app.MapGet("/api/ad/groups",async(IntegrationSettings settings,CancellationToken token)=>
        {
            var options=settings.Ad();
            if(!options.Enabled||string.IsNullOrWhiteSpace(options.AdminUser)||string.IsNullOrWhiteSpace(options.AdminPassword)) return Results.Ok(Array.Empty<object>());
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var ldap=AdAuthentication.Connection(options);
            await ldap.ConnectAsync(options.Host,options.Port,timeout.Token);
            await ldap.BindAsync(options.AdminUser.Contains('@')||options.AdminUser.Contains('\\')?options.AdminUser:options.AdminUser+"@"+options.Domain,options.AdminPassword,timeout.Token);
            var constraints=ldap.SearchConstraints;constraints.ReferralFollowing=false;ldap.Constraints=constraints;
            var result=await ldap.SearchAsync(options.BaseDn,LdapConnection.ScopeSub,"(objectClass=group)",["cn"],false,timeout.Token);
            var groups=new List<object>();
            while(await result.HasMoreAsync(timeout.Token))
            {
                try {var entry=await result.NextAsync(timeout.Token);groups.Add(new {value="ad:"+entry.Dn,label=entry.Get("cn").StringValue});}
                catch(LdapReferralException) { }
            }
            return Results.Ok(groups);
        }).RequireAuthorization("settings.manage");
    }
}