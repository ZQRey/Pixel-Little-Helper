namespace LitleHelperServer;
public static class CabinetValidation
{
    public static string Resolve(string? requested, string? stored, string machine)
    {
        var room = (string.IsNullOrWhiteSpace(requested) ? stored : requested)?.Trim() ?? "";
        if (room.Length is < 1 or > 50 || room.Equals(machine, StringComparison.OrdinalIgnoreCase) || room.Any(char.IsControl))
            throw new Microsoft.AspNetCore.SignalR.HubException("Не указан корректный номер кабинета. Укажите кабинет перед вызовом (1–50 символов).");
        return room;
    }
}