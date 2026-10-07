using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace LitleHelperServer;

public static class Access
{
    public static readonly Dictionary<string, string> Catalog = new()
    {
        ["computers.view"] = "Просмотр компьютеров",
        ["computers.manage"] = "Подключение компьютеров",
        ["computers.delete"] = "Удаление компьютеров",
        ["inventory.view"] = "Просмотр инвентаря",
        ["commands.execute"] = "Скрипты, питание и процессы",
        ["terminal.execute"] = "CMD / PowerShell и командные кнопки",
        ["buttons.manage"] = "Редактирование меню помощника",
        ["tickets.all"] = "Просмотр всех заявок",
        ["tickets.manage"] = "Создание заявок и изменение статуса",
        ["audit.view"] = "Просмотр своего журнала команд",
        ["audit.all"] = "Просмотр и очистка общего журнала",
        ["users.manage"] = "Управление пользователями и всеми правами",
        ["settings.manage"] = "Настройки GLPI, AD, Telegram и филиалов",
        ["updates.manage"] = "Публикация обновлений клиента",
        ["chat.broadcast"] = "Рассылка всем пользователям мессенджера"
    };
    public static string[] Defaults(string role) => role switch
    {
        Roles.SuperAdmin => Catalog.Keys.ToArray(),
        Roles.Admin => ["computers.view", "computers.manage", "commands.execute", "buttons.manage", "tickets.all", "tickets.manage", "audit.view"],
        Roles.Operator => ["computers.view", "tickets.all", "tickets.manage"],
        _ => []
    };
    public static string[] Effective(PanelUser user) => user.Role == Roles.SuperAdmin ? Defaults(user.Role) :
        Catalog.Keys.Where(key => user.Permissions.TryGetValue(key, out bool value) ? value : Defaults(user.Role).Contains(key)).ToArray();
    public static bool Can(ClaimsPrincipal user, string key) => user.HasClaim("permission", key);
    public static bool Can(PanelUser user, string key) => Effective(user).Contains(key);
    public static void Validate(Dictionary<string, bool>? permissions)
    {
        if (permissions != null && permissions.Keys.Any(key => !Catalog.ContainsKey(key))) throw new ArgumentException("Неизвестное право доступа");
    }
    public static async Task EnsureSchema(HelperDb db)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"PermissionOverrides\" TEXT NOT NULL DEFAULT '{{}}'");
        else
        {
            await db.Database.OpenConnectionAsync();
            using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = "PRAGMA table_info('Users')";
            bool exists = false;
            using (var reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync()) if (reader.GetString(1) == "PermissionOverrides") exists = true;
            if (!exists) await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN \"PermissionOverrides\" TEXT NOT NULL DEFAULT '{{}}'");
            await db.Database.CloseConnectionAsync();
        }
    }
}
