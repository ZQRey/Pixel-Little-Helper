using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
namespace LitleHelperServer;
public static class SystemButtons
{
    public static async Task EnsureAsync(HelperDb db)
    {
        await ChatBroadcasts.Column(db, "Computers", "SupportsSystemButtons", db.Database.IsNpgsql() ? "BOOLEAN NOT NULL DEFAULT false" : "INTEGER NOT NULL DEFAULT 0");
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Buttons\" ADD COLUMN IF NOT EXISTS \"SystemKey\" TEXT NULL");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Buttons\" ADD COLUMN IF NOT EXISTS \"IsSystem\" boolean NOT NULL DEFAULT false");
        }
        else
        {
            await db.Database.OpenConnectionAsync();
            var columns = new HashSet<string>();
            using (var cmd = db.Database.GetDbConnection().CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info('Buttons')";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            }
            if (!columns.Contains("SystemKey")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Buttons\" ADD COLUMN \"SystemKey\" TEXT NULL");
            if (!columns.Contains("IsSystem")) await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Buttons\" ADD COLUMN \"IsSystem\" INTEGER NOT NULL DEFAULT 0");
            await db.Database.CloseConnectionAsync();
        }
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Buttons_SystemKey\" ON \"Buttons\" (\"SystemKey\")");
        foreach (var item in new[] { ("messenger", "Мессенджер"), ("emergency", "Экстренное оповещение") })
            if (!await db.Buttons.AnyAsync(b => b.SystemKey == item.Item1))
                db.Buttons.Add(new() { SystemKey = item.Item1, IsSystem = true, Title = item.Item2, ActionType = item.Item1, IconName = item.Item1, OrderIndex = 10000 });
        await db.SaveChangesAsync();
    }
    public static async Task<int> NotifyAsync(HelperDb db, IHubContext<HelperHub> hub)
    {
        var computers = await db.Computers.AsNoTracking().Where(c => c.IsOnline && c.ConnectionId != null).ToListAsync();
        foreach (var c in computers) await hub.Clients.Client(c.ConnectionId!).SendAsync("OnButtonsUpdated", await HelperHub.Buttons(db, c));
        return computers.Count;
    }
}