using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace LitleHelperServer;

public sealed record CreatePollRequest(int Peer, string Question, List<string> Options, bool MultipleChoice = false, bool IsAnonymous = false);
public sealed record VotePollRequest(List<int> OptionIndices);
public sealed record PollOptionResult(int Index, string Text, int VoteCount, double Percentage, List<string>? Voters);
public sealed record PollDetails(int Id, int Peer, int CreatorId, string CreatorName, string Question, bool MultipleChoice, bool IsAnonymous, bool IsClosed, int TotalVoters, List<PollOptionResult> Options, List<int> MyVotes, DateTime CreatedAt);

public static class ChatPolls
{
    public static async Task EnsureSchemaAsync(HelperDb db)
    {
        string id = db.Database.IsNpgsql() ? "BIGSERIAL" : "INTEGER";
        string boolean = db.Database.IsNpgsql() ? "boolean" : "INTEGER";
        string date = db.Database.IsNpgsql() ? "timestamp with time zone" : "TEXT";

        await db.Database.ExecuteSqlRawAsync($"CREATE TABLE IF NOT EXISTS \"ChatPolls\" (\"Id\" {id} PRIMARY KEY, \"Peer\" INTEGER NOT NULL, \"CreatorId\" INTEGER NOT NULL, \"Question\" TEXT NOT NULL, \"OptionsJson\" TEXT NOT NULL, \"MultipleChoice\" {boolean} NOT NULL, \"IsAnonymous\" {boolean} NOT NULL, \"IsClosed\" {boolean} NOT NULL, \"CreatedAt\" {date} NOT NULL)");
        await db.Database.ExecuteSqlRawAsync($"CREATE TABLE IF NOT EXISTS \"ChatPollVotes\" (\"Id\" {id} PRIMARY KEY, \"PollId\" INTEGER NOT NULL, \"UserId\" INTEGER NOT NULL, \"OptionIndex\" INTEGER NOT NULL, \"VotedAt\" {date} NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_ChatPolls_Peer\" ON \"ChatPolls\" (\"Peer\")");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_ChatPollVotes_PollId\" ON \"ChatPollVotes\" (\"PollId\")");
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_ChatPollVotes_Poll_User_Option\" ON \"ChatPollVotes\" (\"PollId\", \"UserId\", \"OptionIndex\")");
    }

    public static async Task<PollDetails> GetPollDetailsAsync(ChatPoll poll, HelperDb db, int currentUserId)
    {
        var options = JsonSerializer.Deserialize<List<string>>(poll.OptionsJson) ?? [];
        var votes = await db.ChatPollVotes.Where(v => v.PollId == poll.Id).ToListAsync();
        int totalVoters = votes.Select(v => v.UserId).Distinct().Count();

        var creator = await db.Users.FindAsync(poll.CreatorId);
        string creatorName = creator?.FullName ?? "Сотрудник";

        Dictionary<int, string> userNames = new();
        if (!poll.IsAnonymous)
        {
            var userIds = votes.Select(v => v.UserId).Distinct().ToList();
            userNames = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
        }

        var optionResults = new List<PollOptionResult>();
        for (int i = 0; i < options.Count; i++)
        {
            var optionVotes = votes.Where(v => v.OptionIndex == i).ToList();
            int count = optionVotes.Count;
            double pct = totalVoters > 0 ? Math.Round((double)count / totalVoters * 100.0, 1) : 0;
            List<string>? voters = poll.IsAnonymous ? null : optionVotes.Select(v => userNames.TryGetValue(v.UserId, out var name) ? name : "Пользователь").ToList();
            optionResults.Add(new PollOptionResult(i, options[i], count, pct, voters));
        }

        var myVotes = votes.Where(v => v.UserId == currentUserId).Select(v => v.OptionIndex).ToList();
        return new PollDetails(poll.Id, poll.Peer, poll.CreatorId, creatorName, poll.Question, poll.MultipleChoice, poll.IsAnonymous, poll.IsClosed, totalVoters, optionResults, myVotes, poll.CreatedAt);
    }

    public static async Task ValidatePollAccess(HelperDb db, int userId, ChatPoll poll)
    {
        if (poll.Peer < 0)
        {
            await ChatGroups.Member(db, -poll.Peer, userId);
        }
        else
        {
            if (userId != poll.CreatorId && userId != poll.Peer)
                throw new UnauthorizedAccessException("У вас нет доступа к этому опросу.");
        }
    }

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/polls", async (CreatePollRequest request, HelperDb db, ClaimsPrincipal p, IHubContext<MessengerHub> hub, ChatGroupGate gate) =>
        {
            var me = await Messenger.UserAsync(db, p);
            await ChatExtras.ValidatePeer(db, me.Id, request.Peer);

            string question = (request.Question ?? "").Trim();
            if (question.Length is < 3 or > 300)
                return Results.BadRequest(new { error = "Вопрос опроса должен содержать от 3 до 300 символов." });

            var cleanedOptions = (request.Options ?? []).Select(o => (o ?? "").Trim()).Where(o => !string.IsNullOrEmpty(o)).Take(10).ToList();
            if (cleanedOptions.Count < 2)
                return Results.BadRequest(new { error = "Опрос должен содержать минимум 2 варианта ответа." });

            var poll = new ChatPoll
            {
                Peer = request.Peer,
                CreatorId = me.Id,
                Question = question,
                OptionsJson = JsonSerializer.Serialize(cleanedOptions),
                MultipleChoice = request.MultipleChoice,
                IsAnonymous = request.IsAnonymous,
                IsClosed = false,
                CreatedAt = DateTime.UtcNow
            };

            await gate.Semaphore.WaitAsync();
            try
            {
                db.ChatPolls.Add(poll);
                await db.SaveChangesAsync();

                var users = request.Peer < 0
                    ? await db.ChatGroupMembers.Where(m => m.GroupId == -request.Peer).Select(m => m.UserId).ToListAsync()
                    : new List<int> { me.Id, request.Peer };

                string body = $"📊 {question}";
                string command = $"poll:{poll.Id}";
                string clientId = Guid.NewGuid().ToString("N");

                if (request.Peer < 0)
                {
                    var groupMessage = new ChatGroupMessage
                    {
                        GroupId = -request.Peer,
                        SenderId = me.Id,
                        Body = body,
                        Command = command,
                        ClientId = clientId,
                        SentAt = DateTime.UtcNow
                    };
                    db.ChatGroupMessages.Add(groupMessage);
                    await db.SaveChangesAsync();

                    var vm = new ChatMessage
                    {
                        Id = groupMessage.Id,
                        SenderId = me.Id,
                        RecipientId = request.Peer,
                        Body = body,
                        Command = command,
                        ClientId = clientId,
                        SentAt = groupMessage.SentAt,
                        SenderName = me.FullName
                    };
                    await hub.Clients.Groups(users.Select(u => "Chat:" + u)).SendAsync("ChatMessage", vm);
                }
                else
                {
                    var directMessage = new ChatMessage
                    {
                        SenderId = me.Id,
                        RecipientId = request.Peer,
                        Body = body,
                        Command = command,
                        ClientId = clientId,
                        SentAt = DateTime.UtcNow
                    };
                    db.ChatMessages.Add(directMessage);
                    await db.SaveChangesAsync();

                    directMessage.SenderName = me.FullName;
                    await hub.Clients.Groups(users.Select(u => "Chat:" + u)).SendAsync("ChatMessage", directMessage);
                }

                await hub.Clients.Groups(users.Select(u => "Chat:" + u)).SendAsync("PollUpdated", new { pollId = poll.Id, peer = poll.Peer });
                return Results.Ok(await GetPollDetailsAsync(poll, db, me.Id));
            }
            finally
            {
                gate.Semaphore.Release();
            }
        });

        api.MapGet("/polls/{id:int}", async (int id, HelperDb db, ClaimsPrincipal p) =>
        {
            var me = await Messenger.UserAsync(db, p);
            var poll = await db.ChatPolls.FindAsync(id);
            if (poll == null) return Results.NotFound(new { error = "Опрос не найден." });
            await ValidatePollAccess(db, me.Id, poll);

            return Results.Ok(await GetPollDetailsAsync(poll, db, me.Id));
        });

        api.MapPost("/polls/{id:int}/vote", async (int id, VotePollRequest request, HelperDb db, ClaimsPrincipal p, IHubContext<MessengerHub> hub, ChatGroupGate gate) =>
        {
            var me = await Messenger.UserAsync(db, p);
            var poll = await db.ChatPolls.FindAsync(id);
            if (poll == null) return Results.NotFound(new { error = "Опрос не найден." });
            if (poll.IsClosed) return Results.BadRequest(new { error = "Опрос уже завершён." });
            await ValidatePollAccess(db, me.Id, poll);

            var options = JsonSerializer.Deserialize<List<string>>(poll.OptionsJson) ?? [];
            if (request.OptionIndices == null || request.OptionIndices.Count == 0)
                return Results.BadRequest(new { error = "Выберите вариант для голосования." });
            if (!poll.MultipleChoice && request.OptionIndices.Count > 1)
                return Results.BadRequest(new { error = "В этом опросе разрешён только один вариант ответа." });
            if (request.OptionIndices.Any(idx => idx < 0 || idx >= options.Count))
                return Results.BadRequest(new { error = "Недопустимый вариант ответа." });

            await gate.Semaphore.WaitAsync();
            try
            {
                var existingVotes = await db.ChatPollVotes.Where(v => v.PollId == id && v.UserId == me.Id).ToListAsync();
                db.ChatPollVotes.RemoveRange(existingVotes);

                foreach (int idx in request.OptionIndices.Distinct())
                {
                    db.ChatPollVotes.Add(new ChatPollVote
                    {
                        PollId = id,
                        UserId = me.Id,
                        OptionIndex = idx,
                        VotedAt = DateTime.UtcNow
                    });
                }
                await db.SaveChangesAsync();

                var users = poll.Peer < 0
                    ? await db.ChatGroupMembers.Where(m => m.GroupId == -poll.Peer).Select(m => m.UserId).ToListAsync()
                    : new List<int> { poll.CreatorId, poll.Peer };
                await hub.Clients.Groups(users.Select(u => "Chat:" + u)).SendAsync("PollUpdated", new { pollId = poll.Id, peer = poll.Peer });

                return Results.Ok(await GetPollDetailsAsync(poll, db, me.Id));
            }
            finally
            {
                gate.Semaphore.Release();
            }
        });

        api.MapPost("/polls/{id:int}/close", async (int id, HelperDb db, ClaimsPrincipal p, IHubContext<MessengerHub> hub, ChatGroupGate gate) =>
        {
            var me = await Messenger.UserAsync(db, p);
            var poll = await db.ChatPolls.FindAsync(id);
            if (poll == null) return Results.NotFound(new { error = "Опрос не найден." });
            await ValidatePollAccess(db, me.Id, poll);
            if (poll.CreatorId != me.Id && me.Role != Roles.SuperAdmin && me.Role != Roles.Admin)
                return Results.Forbid();

            poll.IsClosed = true;
            await db.SaveChangesAsync();

            var users = poll.Peer < 0
                ? await db.ChatGroupMembers.Where(m => m.GroupId == -poll.Peer).Select(m => m.UserId).ToListAsync()
                : new List<int> { poll.CreatorId, poll.Peer };
            await hub.Clients.Groups(users.Select(u => "Chat:" + u)).SendAsync("PollUpdated", new { pollId = poll.Id, peer = poll.Peer });

            return Results.Ok(await GetPollDetailsAsync(poll, db, me.Id));
        });
    }
}
