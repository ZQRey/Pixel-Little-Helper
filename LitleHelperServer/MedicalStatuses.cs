using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using System.Text.RegularExpressions;
namespace LitleHelperServer;

public class MedicalStatusConfig
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Emoji { get; set; } = "";
    public string ColorHex { get; set; } = "#64748B";
    public bool MuteSounds { get; set; }
    public bool MuteTts { get; set; }
    public int OrderIndex { get; set; }
    public bool IsActive { get; set; } = true;
    public string Description { get; set; } = "";
}
public static class MedicalStatuses
{
    public static async Task EnsureAsync(HelperDb db)
    {
        string id = db.Database.IsNpgsql() ? "SERIAL PRIMARY KEY" : "INTEGER PRIMARY KEY AUTOINCREMENT";
        string flag = db.Database.IsNpgsql() ? "BOOLEAN" : "INTEGER";
        string sql = "CREATE TABLE IF NOT EXISTS \"MedicalStatuses\" (\"Id\" " + id + ", \"Key\" TEXT NOT NULL, \"Title\" TEXT NOT NULL, \"Emoji\" TEXT NOT NULL, \"ColorHex\" TEXT NOT NULL, \"MuteSounds\" " + flag + " NOT NULL, \"MuteTts\" " + flag + " NOT NULL, \"OrderIndex\" INTEGER NOT NULL, \"IsActive\" " + flag + " NOT NULL, \"Description\" TEXT NOT NULL)";
        await db.Database.ExecuteSqlRawAsync(sql);
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_MedicalStatuses_Key\" ON \"MedicalStatuses\" (\"Key\")");
        if (!await db.MedicalStatuses.AnyAsync())
        {
            db.MedicalStatuses.AddRange(
                new() { Key="Available", Title="На приёме пациентов", Emoji="🟢", ColorHex="#16A34A" },
                new() { Key="InSurgery", Title="В операционной / На процедурах", Emoji="🔴", ColorHex="#DC2626", MuteSounds=true, MuteTts=true, OrderIndex=1 },
                new() { Key="InConsultation", Title="На консилиуме / Совещании", Emoji="🟡", ColorHex="#CA8A04", MuteSounds=true, MuteTts=true, OrderIndex=2 },
                new() { Key="Break", Title="Обед / Перерыв", Emoji="⚪", ColorHex="#64748B", OrderIndex=3 });
            await db.SaveChangesAsync();
        }
    }
    private static void Validate(MedicalStatusConfig value)
    {
        if (!Regex.IsMatch(value.Key, "^[A-Za-z][A-Za-z0-9_]{0,49}$") || string.IsNullOrWhiteSpace(value.Title) || value.Title.Length>100 ||
            value.Emoji.Length>16 || !Regex.IsMatch(value.ColorHex, "^#[0-9A-Fa-f]{6}$") || value.Description.Length>1000 || value.OrderIndex is <0 or >10000)
            throw new ArgumentException("Проверьте ключ, название и цвет статуса.");
        if (value.Key == "Available" && (!value.IsActive || value.MuteSounds || value.MuteTts))
            throw new ArgumentException("Статус Available должен оставаться активным без тихого режима.");
    }
    public static async Task Notify(HelperDb db, IHubContext<HelperHub> hub) =>
        await hub.Clients.Group("AllAgents").SendAsync("MedicalStatusesUpdated", await db.MedicalStatuses.AsNoTracking().OrderBy(s=>s.OrderIndex).ToListAsync());
    public static void MapMedicalStatuses(this WebApplication app)
    {
        app.MapGet("/api/settings/medical-statuses", async (HelperDb db) => await db.MedicalStatuses.AsNoTracking().OrderBy(s=>s.OrderIndex).ToListAsync()).RequireAuthorization("settings.manage");
        app.MapPost("/api/settings/medical-statuses", async (MedicalStatusConfig value, HelperDb db, IHubContext<HelperHub> hub) =>
        {
            Validate(value);
            if (await db.MedicalStatuses.AnyAsync(s=>s.Key==value.Key)) return Results.Conflict("Ключ статуса уже существует.");
            value.Id=0; db.MedicalStatuses.Add(value); await db.SaveChangesAsync(); await Notify(db,hub); return Results.Ok(value);
        }).RequireAuthorization("settings.manage");
        app.MapPut("/api/settings/medical-statuses/{id:int}", async (int id, MedicalStatusConfig value, HelperDb db, IHubContext<HelperHub> hub) =>
        {
            var old=await db.MedicalStatuses.FindAsync(id); if(old==null) return Results.NotFound();
            value.Key=old.Key; Validate(value);
            old.Title=value.Title; old.Emoji=value.Emoji; old.ColorHex=value.ColorHex; old.MuteSounds=value.MuteSounds; old.MuteTts=value.MuteTts;
            old.OrderIndex=value.OrderIndex; old.IsActive=value.IsActive; old.Description=value.Description;
            await db.SaveChangesAsync(); await Notify(db,hub); return Results.Ok(old);
        }).RequireAuthorization("settings.manage");
        app.MapDelete("/api/settings/medical-statuses/{id:int}", async (int id, HelperDb db, IHubContext<HelperHub> hub) =>
        {
            var old=await db.MedicalStatuses.FindAsync(id); if(old==null) return Results.NotFound();
            if(old.Key=="Available") return Results.Conflict("Нельзя удалить системный статус Available.");
            db.Remove(old); await db.SaveChangesAsync(); await Notify(db,hub); return Results.NoContent();
        }).RequireAuthorization("settings.manage");
    }
}