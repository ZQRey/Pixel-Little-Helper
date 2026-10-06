using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LitleHelperServer;

public class CommandService(HelperDb db, IHubContext<HelperHub> hub, IConfiguration config)
{
    public async Task<List<AuditLog>> SendAsync(ClaimsPrincipal principal, CommandRequest request, CancellationToken token)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Username == principal.Identity!.Name, token);
        if (user == null || !user.IsActive || user.MustChangePassword || !principal.HasClaim("version", user.SecurityVersion.ToString()))
            throw new UnauthorizedAccessException("Требуется повторный вход");
        bool super = user.Role == Roles.SuperAdmin;
        if (!super && user.Role != Roles.Admin) throw new UnauthorizedAccessException("Недостаточно прав");
        if (request.Machines.Length is < 1 or > 500 || request.Payload.Length > 8000) throw new ArgumentException("Неверный размер команды");
        string type = request.Type, payload = request.Payload;
        if (type is "cmd" or "powershell")
        {
            if (!super) throw new UnauthorizedAccessException("Терминал доступен только SuperAdmin");
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
        else if (type is not ("reboot" or "shutdown" or "inventory")) throw new ArgumentException("Неверный тип команды");
        var machines = request.Machines.Select(Security.Canonical).Distinct().ToArray();
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
