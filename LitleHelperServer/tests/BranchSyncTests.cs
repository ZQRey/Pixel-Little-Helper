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

    public static async Task RunAsync()
    {
        using var db = new HelperDb(new DbContextOptionsBuilder<HelperDb>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

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

        Console.WriteLine("All BranchSync tests passed successfully.");
    }
}
