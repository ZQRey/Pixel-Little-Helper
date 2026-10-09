using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Security.Claims;

namespace LitleHelperServer;

public class Branch
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string ComputerPrefixes { get; set; } = "";
    public string IpSubnets { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
public static class Branches
{
    public static bool MatchesIpSubnet(string? pattern, string? ipStr)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(ipStr)) return false;
        if (!IPAddress.TryParse(ipStr.Trim(), out var ip)) return false;
        var bytes = ip.GetAddressBytes();

        var subnets = pattern.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var sub in subnets)
        {
            if (sub.Contains('*'))
            {
                string prefix = sub.Replace("*", "").TrimEnd('.');
                if (ipStr.Trim().StartsWith(prefix)) return true;
            }
            else if (sub.Contains('/'))
            {
                var parts = sub.Split('/');
                if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var netIp) && int.TryParse(parts[1], out int maskBits))
                {
                    var netBytes = netIp.GetAddressBytes();
                    if (netBytes.Length == bytes.Length && maskBits is >= 0 and <= 32)
                    {
                        uint mask = maskBits == 0 ? 0 : uint.MaxValue << (32 - maskBits);
                        uint ipVal = BitConverter.ToUInt32(bytes.Reverse().ToArray(), 0);
                        uint netVal = BitConverter.ToUInt32(netBytes.Reverse().ToArray(), 0);
                        if ((ipVal & mask) == (netVal & mask)) return true;
                    }
                }
            }
            else if (string.Equals(sub, ipStr.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public static Branch? FindByComputerName(IEnumerable<Branch> branches, string machineName, string? ipAddress = null)
    {
        if (string.IsNullOrWhiteSpace(machineName)) return null;
        string name = machineName.Trim().ToUpperInvariant();
        var activeBranches = branches.Where(b => b.IsActive).ToList();

        // Level 1: Match by IP subnet if present
        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            foreach (var b in activeBranches)
            {
                if (MatchesIpSubnet(b.IpSubnets, ipAddress)) return b;
            }
        }

        // Level 2: Longest Prefix Matching across all active branches
        var prefixPairs = activeBranches
            .SelectMany(b => (b.ComputerPrefixes ?? "")
                .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => (Prefix: p.ToUpperInvariant(), Branch: b)))
            .OrderByDescending(x => x.Prefix.Length)
            .ToList();

        foreach (var pair in prefixPairs)
        {
            if (name.StartsWith(pair.Prefix)) return pair.Branch;
        }

        return null;
    }

    public static async Task<object> SyncAllAsync(HelperDb db)
    {
        var activeBranches = await db.Branches.AsNoTracking().Where(b => b.IsActive).ToListAsync();
        var computers = await db.Computers.ToListAsync();
        var users = await db.Users.ToListAsync();
        int updatedComputers = 0, updatedUsers = 0;

        foreach (var c in computers)
        {
            var matched = FindByComputerName(activeBranches, c.MachineName, c.IpAddress);
            if (matched != null && c.BranchId != matched.Id)
            {
                c.BranchId = matched.Id;
                updatedComputers++;
            }
        }

        foreach (var u in users)
        {
            // SuperAdmins and Admins have their branch configured strictly manually
            if (u.Role is Roles.SuperAdmin or Roles.Admin) continue;

            string uNorm = Security.NormalizeAccount(u.Username);
            var matchedPc = computers.FirstOrDefault(c =>
                !string.IsNullOrWhiteSpace(c.CurrentUser) &&
                (Security.NormalizeAccount(c.CurrentUser) == uNorm ||
                 (!string.IsNullOrWhiteSpace(u.FullName) && !string.IsNullOrWhiteSpace(c.CurrentUserFullName) &&
                  string.Equals(u.FullName.Trim(), c.CurrentUserFullName.Trim(), StringComparison.OrdinalIgnoreCase))));

            if (matchedPc != null)
            {
                if (matchedPc.BranchId != null && u.BranchId != matchedPc.BranchId)
                {
                    u.BranchId = matchedPc.BranchId;
                    updatedUsers++;
                }
                if (string.IsNullOrWhiteSpace(u.Room) && !string.IsNullOrWhiteSpace(matchedPc.Room))
                {
                    u.Room = matchedPc.Room;
                }
                else if (!string.IsNullOrWhiteSpace(u.Room) && string.IsNullOrWhiteSpace(matchedPc.Room))
                {
                    matchedPc.Room = u.Room;
                }
            }
        }

        // Auto-create/import missing AD users from active computers that have a branch assigned
        foreach (var c in computers)
        {
            if (string.IsNullOrWhiteSpace(c.CurrentUser) || c.BranchId == null) continue;
            string cNorm = Security.NormalizeAccount(c.CurrentUser);
            if (string.IsNullOrWhiteSpace(cNorm)) continue;

            bool exists = users.Any(u => Security.NormalizeAccount(u.Username) == cNorm ||
                (!string.IsNullOrWhiteSpace(u.FullName) && !string.IsNullOrWhiteSpace(c.CurrentUserFullName) &&
                 string.Equals(u.FullName.Trim(), c.CurrentUserFullName.Trim(), StringComparison.OrdinalIgnoreCase)));

            if (!exists)
            {
                var newUser = new PanelUser
                {
                    Username = c.CurrentUser,
                    FullName = !string.IsNullOrWhiteSpace(c.CurrentUserFullName) ? c.CurrentUserFullName.Trim() : c.CurrentUser,
                    Role = Roles.User,
                    BranchId = c.BranchId,
                    Room = c.Room,
                    IsActive = true,
                    PasswordHash = "!AD"
                };
                db.Users.Add(newUser);
                users.Add(newUser);
                updatedUsers++;
            }
        }

        await db.SaveChangesAsync();
        return new { success = true, updatedComputers, updatedUsers, message = $"Синхронизировано: {updatedComputers} рабочих станций, {updatedUsers} пользователей." };
    }

    public static bool CanHandle(PanelUser user, TicketRecord ticket) => user.Role == Roles.SuperAdmin || (user.Role == Roles.Admin && user.BranchId == null) || ticket.BranchId == null || user.BranchId == ticket.BranchId;
    public static void Require(PanelUser user, TicketRecord ticket)
    { if (!CanHandle(user, ticket)) throw new UnauthorizedAccessException("Заявка относится к другому филиалу. Проверьте филиал в профиле пользователя."); }
    public static async Task ValidateUserAsync(HelperDb db, int? id)
    { if (id != null && !await db.Branches.AnyAsync(b => b.Id == id)) throw new ArgumentException("Выберите действующий филиал."); }
    public static async Task<Branch?> ValidateTicketAsync(HelperDb db, int? id, string room)
    {
        if (room.Length > 100 || room.Any(char.IsControl)) throw new ArgumentException("Кабинет: до 100 символов без переносов строк.");
        if (id == null)
        {
            if (await db.Branches.AnyAsync(b => b.IsActive)) throw new ArgumentException("Выберите филиал и укажите кабинет. Обновите клиент, если этих полей нет.");
            return null;
        }
        var branch = await db.Branches.SingleOrDefaultAsync(b => b.Id == id && b.IsActive) ?? throw new ArgumentException("Филиал недоступен. Обновите список филиалов.");
        if (string.IsNullOrWhiteSpace(room)) throw new ArgumentException("Укажите кабинет.");
        return branch;
    }
    public static string Description(string text, Branch? branch, string room) => branch == null ? text : "Филиал: " + branch.Name + "\nКабинет: " + room.Trim() + "\n\n" + text;
    public static async Task EnsureSchemaAsync(HelperDb db)
    {
        bool pg = db.Database.IsNpgsql();
        string key = pg ? "INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY" : "INTEGER PRIMARY KEY AUTOINCREMENT";
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS \"Branches\" (\"Id\" " + key + ", \"Name\" TEXT NOT NULL, \"IsActive\" " + (pg ? "BOOLEAN" : "INTEGER") + " NOT NULL)");
        foreach (var (table, column, type) in new[] {
            ("Branches", "ComputerPrefixes", "TEXT NOT NULL DEFAULT ''"),
            ("Branches", "IpSubnets", "TEXT NOT NULL DEFAULT ''"),
            ("Users", "BranchId", "INTEGER NULL"),
            ("Users", "Room", "TEXT NOT NULL DEFAULT ''"),
            ("Computers", "BranchId", "INTEGER NULL"),
            ("Computers", "Room", "TEXT NOT NULL DEFAULT ''"),
            ("Tickets", "BranchId", "INTEGER NULL"),
            ("Tickets", "BranchName", "TEXT NOT NULL DEFAULT ''"),
            ("Tickets", "Room", "TEXT NOT NULL DEFAULT ''")
        })
        {
            if (pg) await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"" + table + "\" ADD COLUMN IF NOT EXISTS \"" + column + "\" " + type);
            else
            {
                await db.Database.OpenConnectionAsync(); using var cmd = db.Database.GetDbConnection().CreateCommand(); cmd.CommandText = $"PRAGMA table_info('{table}')";
                bool found = false; using (var reader = await cmd.ExecuteReaderAsync()) while (await reader.ReadAsync()) if (reader.GetString(1) == column) found = true;
                if (!found) await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"" + table + "\" ADD COLUMN \"" + column + "\" " + type);
                await db.Database.CloseConnectionAsync();
            }
        }
    }
    public static void MapBranchApi(this WebApplication app)
    {
        app.MapGet("/api/branches", async (HelperDb db) => Results.Ok(await db.Branches.AsNoTracking().OrderBy(b => b.Name).ToListAsync())).RequireAuthorization("Panel");
        app.MapPost("/api/branches/sync", async (HelperDb db) => Results.Ok(await SyncAllAsync(db))).RequireAuthorization("settings.manage");
        app.MapPost("/api/branches", async (Branch branch, HelperDb db) =>
        {
            await ValidateAsync(branch, db); branch.Id = 0; db.Branches.Add(branch);
            await db.SaveChangesAsync();
            _ = SyncAllAsync(db);
            return Results.Ok(branch);
        }).RequireAuthorization("settings.manage");
        app.MapPut("/api/branches/{id:int}", async (int id, Branch input, HelperDb db) =>
        {
            input.Id = id; await ValidateAsync(input, db); var branch = await db.Branches.FindAsync(id); if (branch == null) return Results.NotFound();
            branch.Name = input.Name; branch.ComputerPrefixes = input.ComputerPrefixes; branch.IpSubnets = input.IpSubnets; branch.IsActive = input.IsActive;
            await db.SaveChangesAsync();
            _ = SyncAllAsync(db);
            return Results.Ok(branch);
        }).RequireAuthorization("settings.manage");
        app.MapDelete("/api/branches/{id:int}", async (int id, HelperDb db) =>
        {
            var branch = await db.Branches.FindAsync(id); if (branch == null) return Results.NotFound();
            if (await db.Users.AnyAsync(u => u.BranchId == id) || await db.Tickets.AnyAsync(t => t.BranchId == id)) return Results.Conflict(new { error = "Филиал используется в пользователях или заявках. Отключите его, чтобы сохранить историю." });
            db.Branches.Remove(branch); await db.SaveChangesAsync(); return Results.NoContent();
        }).RequireAuthorization("settings.manage");
    }
    private static async Task ValidateAsync(Branch branch, HelperDb db)
    {
        branch.Name = branch.Name.Trim();
        branch.ComputerPrefixes = (branch.ComputerPrefixes ?? "").Trim();
        branch.IpSubnets = (branch.IpSubnets ?? "").Trim();
        if (branch.Name.Length is < 1 or > 100 || branch.Name.Any(char.IsControl)) throw new ArgumentException("Название филиала: от 1 до 100 символов.");
        if (branch.ComputerPrefixes.Length > 200 || branch.ComputerPrefixes.Any(char.IsControl)) throw new ArgumentException("Префиксы ПК: до 200 символов без управляющих символов.");
        if (branch.IpSubnets.Length > 255 || branch.IpSubnets.Any(char.IsControl)) throw new ArgumentException("IP-подсети: до 255 символов.");
        if (await db.Branches.AnyAsync(b => b.Id != branch.Id && b.Name.ToLower() == branch.Name.ToLower())) throw new ArgumentException("Филиал с таким названием уже существует.");
    }
}

