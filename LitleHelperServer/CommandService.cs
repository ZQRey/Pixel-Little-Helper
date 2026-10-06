using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace LitleHelperServer;

public class CommandService(HelperDb db, IHubContext<HelperHub> hub, IConfiguration config)
{
    public async Task<List<AuditLog>> SendAsync(ClaimsPrincipal principal, CommandRequest request, CancellationToken token)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Username == principal.Identity!.Name, token);
        if (user == null || !user.IsActive || user.MustChangePassword || !principal.HasClaim("version", user.SecurityVersion.ToString()))
            throw new UnauthorizedAccessException("Требуется повторный вход");
        bool super = Access.Can(user, "terminal.execute");
        if (!Access.Can(user, "commands.execute")) throw new UnauthorizedAccessException("Недостаточно прав");
        if (request.Machines.Length is < 1 or > 500 || request.Payload.Length > 8000) throw new ArgumentException("Неверный размер команды");
        string type = request.Type, payload = request.Payload;
        if (type is "cmd" or "powershell")
        {
            if (!super) throw new UnauthorizedAccessException("Не выдано право CMD / PowerShell");
            if (string.IsNullOrWhiteSpace(payload)) throw new ArgumentException("Введите команду");
        }
        else if (type == "script")
        {
            var script = config.GetSection("Scripts").GetChildren().SingleOrDefault(s => s.Key == payload);
            if (script == null) throw new ArgumentException("Предустановленный скрипт не найден");
            payload = script["Command"]!; // Trusted server configuration, never command text from an Admin.
        }
        else if (type == "kill")
        {
            payload = payload.Trim();
            if (payload.Length is < 1 or > 100 || payload.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-')))
                throw new ArgumentException("Неверное имя процесса");
        }
        else if (type == "kill_pid")
        {
            try
            {
                using var target = JsonDocument.Parse(payload);
                if (!target.RootElement.TryGetProperty("pid", out var pid) || !pid.TryGetInt32(out int id) || id <= 4 ||
                    !target.RootElement.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) || name.GetString()!.Length > 260 ||
                    !target.RootElement.TryGetProperty("startTimeUtcTicks", out var started) || started.ValueKind != JsonValueKind.String || !long.TryParse(started.GetString(), out long ticks) || ticks <= 0)
                    throw new ArgumentException("Неверные данные выбранного процесса");
            }
            catch (JsonException) { throw new ArgumentException("Неверные данные выбранного процесса"); }
        }
        else if (type == "processes") { payload = ""; }
        else if (type is not ("reboot" or "shutdown" or "inventory")) throw new ArgumentException("Неверный тип команды");
        var machines = request.Machines.Select(Security.Canonical).Distinct().ToArray();
        if (type is "processes" or "kill_pid" && (machines.Length != 1 || machines.Contains("ALL")))
            throw new ArgumentException("Выберите один компьютер для работы со списком процессов");
        var computers = machines.Contains("ALL") ? await db.Computers.ToListAsync(token) :
            await db.Computers.Where(c => machines.Contains(c.MachineName)).ToListAsync(token);
        if (computers.Count == 0) throw new ArgumentException("Компьютеры не найдены");
        if (!machines.Contains("ALL") && computers.Count != machines.Length) throw new ArgumentException("Один из компьютеров не найден");
        List<AuditLog> tasks = [];
        foreach (var computer in computers)
        {
            var log = new AuditLog { AdminUsername = user.Username, MachineName = computer.MachineName,
                CommandType = type, CommandPayload = request.Type == "script" ? request.Payload : payload,
                Status = computer.IsOnline && computer.ConnectionId != null ? "Pending" : "Offline", Result = computer.IsOnline ? "" : "ПК недоступен" };
            db.AuditLogs.Add(log); tasks.Add(log);
        }
        await db.SaveChangesAsync(token); // Persist correlation before the client can reply.
        foreach (var log in tasks.Where(l => l.Status == "Pending"))
        {
            var computer = computers.Single(c => c.MachineName == log.MachineName);
            string wireType = type == "script" ? config[$"Scripts:{request.Payload}:Type"] ?? "cmd" : type;
            await hub.Clients.Client(computer.ConnectionId!).SendAsync("ExecuteCommand", new CommandEnvelope(log.TaskId, wireType, payload), token);
        }
        return tasks;
    }
}
