using Microsoft.EntityFrameworkCore;
using LitleHelperServer;

namespace LitleHelperServer.Tests;

public static class BranchSyncTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }

    private sealed class AgentContext(string machine) : Microsoft.AspNetCore.SignalR.HubCallerContext
    {
        public override string ConnectionId => "ticket-context-test";
        public override string? UserIdentifier => machine;
        public override System.Security.Claims.ClaimsPrincipal User => new(new System.Security.Claims.ClaimsIdentity(
            [new(System.Security.Claims.ClaimTypes.Name, machine), new(System.Security.Claims.ClaimTypes.Role, "Agent")], "Agent"));
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override Microsoft.AspNetCore.Http.Features.IFeatureCollection Features { get; } = new Microsoft.AspNetCore.Http.Features.FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }
    public static async Task RunAsync()
    {
        using var db = new HelperDb(new DbContextOptionsBuilder<HelperDb>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

        await SystemButtons.EnsureAsync(db); await SystemButtons.EnsureAsync(db);
        Check(await db.Buttons.CountAsync(b => b.IsSystem) == 2, "system button seeding is idempotent");
        // 1. Setup branches with prefixes
        var polyclinic = new Branch { Name = "Поликлиника", ComputerPrefixes = "N", IsActive = true };
        var hospital = new Branch { Name = "Больница", ComputerPrefixes = "B", IsActive = true };
        var maternity = new Branch { Name = "Роддом", ComputerPrefixes = "R", IsActive = true };
        db.Branches.AddRange(polyclinic, hospital, maternity);
        await db.SaveChangesAsync();

        // 2. Setup computers
        var pc1 = new Computer { MachineName = "N2416600816", CurrentUser = "zqrey3", CurrentUserFullName = "Закиров Ренат", Room = "101" };
        var pc2 = new Computer { MachineName = "B2416600817", CurrentUser = "ivanov", CurrentUserFullName = "Иванов Иван", Room = "202" };
        var pc3 = new Computer { MachineName = "R2416600818", CurrentUser = "petrova", CurrentUserFullName = "Петрова Анна", Room = "303" };
        var pcAdmin = new Computer { MachineName = "N9999999999", CurrentUser = "superadmin", CurrentUserFullName = "Главный Админ", Room = "99" };
        db.Computers.AddRange(pc1, pc2, pc3, pcAdmin);

        // 3. Setup users (AD users with UPN / DOMAIN prefix, and Admins)
        var userAdUpn = new PanelUser { Username = "zqrey3@hospital.kz", FullName = "Закиров Ренат", Role = Roles.User, PasswordHash = "!AD" };
        var userAdDomain = new PanelUser { Username = @"HOSPITAL\ivanov", FullName = "Иванов Иван", Role = Roles.User, PasswordHash = "!AD" };
        var superAdmin = new PanelUser { Username = "superadmin", FullName = "Главный Админ", Role = Roles.SuperAdmin, BranchId = null, PasswordHash = "hash" };
        var localAdmin = new PanelUser { Username = "boss", FullName = "Начальник", Role = Roles.Admin, BranchId = hospital.Id, PasswordHash = "hash" };
        db.Users.AddRange(userAdUpn, userAdDomain, superAdmin, localAdmin);
        await db.SaveChangesAsync();

        // 4. Run SyncAllAsync
        var result = await Branches.SyncAllAsync(db);
        Check(result != null, "SyncAllAsync executed successfully");

        // Verify computer branch matching by single letter prefix
        Check(pc1.BranchId == polyclinic.Id, "Computer N2416600816 bound to Polyclinic (prefix N)");
        Check(pc2.BranchId == hospital.Id, "Computer B2416600817 bound to Hospital (prefix B)");
        Check(pc3.BranchId == maternity.Id, "Computer R2416600818 bound to Maternity (prefix R)");

        // Verify user matching by normalized AD login
        Check(userAdUpn.BranchId == polyclinic.Id, "User zqrey3@hospital.kz bound to Polyclinic from computer N2416600816");
        Check(userAdUpn.Room == "101", "User zqrey3@hospital.kz room synced from computer (101)");

        Check(userAdDomain.BranchId == hospital.Id, @"User HOSPITAL\ivanov bound to Hospital from computer B2416600817");
        Check(userAdDomain.Room == "202", @"User HOSPITAL\ivanov room synced from computer (202)");

        // Verify SuperAdmin and Admin branches are NOT overwritten
        Check(superAdmin.BranchId == null, "SuperAdmin branch remains null (never overwritten by sync)");
        Check(localAdmin.BranchId == hospital.Id, "Admin manual branch remains Hospital (never overwritten by sync)");

        // Verify missing AD user 'petrova' auto-created from active computer pc3
        var autoCreated = await db.Users.FirstOrDefaultAsync(u => u.Username == "petrova");
        Check(autoCreated != null, "Missing user petrova auto-created from active computer");
        Check(autoCreated!.BranchId == maternity.Id, "Auto-created user petrova bound to Maternity branch");
        Check(autoCreated.Room == "303", "Auto-created user petrova room set to 303");
        Check(autoCreated.FullName == "Петрова Анна", "Auto-created user petrova has correct FullName");
        Check(autoCreated.AuthSource == "AD", "Auto-created user petrova has AuthSource AD");

        // Exercise the actual hub endpoint with an Agent identity, regardless of the employee's panel role.
        var unknown = new Computer { MachineName = "UNKNOWN", Room = "404" };
        db.Computers.Add(unknown);
        foreach (var item in new[] { (pc1, polyclinic.Id), (pc2, hospital.Id), (pc3, maternity.Id), (unknown, (int?)null) })
        {
            item.Item1.ConnectionId = "ticket-context-test";
            item.Item1.BranchId = null;
            await db.SaveChangesAsync();
            var hub = new HelperHub(db, null!, null!, null!, null!, null!) { Context = new AgentContext(item.Item1.MachineName) };
            var context = System.Text.Json.JsonSerializer.SerializeToElement(await hub.GetTicketContext());
            Check(context.GetProperty("detectedBranchId").ToString() == (item.Item2?.ToString() ?? ""), "GetTicketContext detects branch for " + item.Item1.MachineName);
            Check(context.GetProperty("room").GetString() == item.Item1.Room, "GetTicketContext returns room for " + item.Item1.MachineName);
        }
        pc1.BranchId = hospital.Id; await db.SaveChangesAsync();
        var assignedHub = new HelperHub(db, null!, null!, null!, null!, null!) { Context = new AgentContext(pc1.MachineName) };
        var assigned = System.Text.Json.JsonSerializer.SerializeToElement(await assignedHub.GetTicketContext());
        Check(assigned.GetProperty("detectedBranchId").GetInt32() == hospital.Id, "stored computer branch takes precedence over prefix");

        // 5. Multi-branch administrator tests
        await Branches.EnsureSchemaAsync(db);
        var multiAdmin = new PanelUser { Username = "multi_admin", Role = Roles.Admin, PasswordHash = "hash" };
        multiAdmin.BranchIds = [polyclinic.Id, maternity.Id];
        db.Users.Add(multiAdmin);
        await db.SaveChangesAsync();

        Check(multiAdmin.BranchIds.Length == 2, "multi-admin has 2 assigned branch IDs");
        Check(multiAdmin.BranchId == polyclinic.Id, "multi-admin first branch synced to BranchId legacy field");

        var ticketPoly = new TicketRecord { BranchId = polyclinic.Id, Title = "Polyclinic ticket" };
        var ticketMat = new TicketRecord { BranchId = maternity.Id, Title = "Maternity ticket" };
        var ticketHosp = new TicketRecord { BranchId = hospital.Id, Title = "Hospital ticket" };
        var ticketNoBranch = new TicketRecord { BranchId = null, Title = "Global ticket" };

        Check(Branches.CanHandle(multiAdmin, ticketPoly), "multi-admin can handle Polyclinic ticket");
        Check(Branches.CanHandle(multiAdmin, ticketMat), "multi-admin can handle Maternity ticket");
        Check(!Branches.CanHandle(multiAdmin, ticketHosp), "multi-admin cannot handle Hospital ticket (not assigned)");
        Check(Branches.CanHandle(multiAdmin, ticketNoBranch), "multi-admin can handle unassigned/legacy ticket");

        var globalAdmin = new PanelUser { Username = "global_admin", Role = Roles.Admin, PasswordHash = "hash" };
        globalAdmin.BranchIds = [];
        Check(Branches.CanHandle(globalAdmin, ticketHosp), "admin with no branches can handle all tickets");

        await Branches.ValidateUserBranchesAsync(db, [polyclinic.Id, hospital.Id]);
        Check(true, "ValidateUserBranchesAsync passes for valid branches");
        try
        {
            await Branches.ValidateUserBranchesAsync(db, [999999]);
            throw new Exception("Non-existent branch accepted");
        }
        catch (ArgumentException)
        {
            Check(true, "ValidateUserBranchesAsync throws ArgumentException on non-existent branch");
        }

        Console.WriteLine("All BranchSync and Multi-Branch Admin tests passed successfully.");
    }
}
