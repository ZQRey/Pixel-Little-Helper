using LitleHelperServer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text.Json;

public static class TelegramManagementTests
{
    private static void Check(bool value, string text) { if (!value) throw new Exception(text); Console.WriteLine("PASS " + text); }
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private class Directory : ITelegramDirectory
    {
        public Task<AdIdentity> FindAsync(long id, CancellationToken token) => id switch
        {
            123 => Task.FromResult(new AdIdentity("alice@ad.test", "Alice", "alice")),
            124 => Task.FromResult(new AdIdentity("bob@ad.test", "Bob", "bob")),
            _ => throw new UnauthorizedAccessException("Telegram ID не найден в AD.")
        };
    }
    private class Transport : HttpMessageHandler
    {
        public readonly Dictionary<int, int> Assigned = new();
        public readonly Dictionary<int, int> Status = new();
        public readonly List<(string Chat, string Text)> Messages = new();
        public int Comments, Solutions, Assignments;
        public bool FailAssignment;
        public string LastContent = "";
        public bool HasClaimButton;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string path = request.RequestUri!.AbsolutePath;
            JsonElement body = request.Content == null ? Json(new { }) : JsonDocument.Parse(await request.Content.ReadAsStringAsync(token)).RootElement.Clone();
            object result = new { };
            if (path.StartsWith("/bot"))
            {
                if (path.EndsWith("sendMessage"))
                {
                    Messages.Add((body.GetProperty("chat_id").GetString()!, body.GetProperty("text").GetString()!));
                    HasClaimButton |= body.TryGetProperty("reply_markup", out var keyboard) && keyboard.ToString().Contains("claim:");
                }
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { ok = true, result }) };
            }
            if (path.EndsWith("initSession")) result = new { session_token = "session" };
            else if (path.EndsWith("killSession")) result = true;
            else if (path.EndsWith("search/User"))
            {
                string query = Uri.UnescapeDataString(request.RequestUri.Query);
                if (!query.Contains("[searchtype]=contains") || !query.Contains("[value]=^") || !query.Contains("$&"))
                    throw new Exception("GLPI login lookup must use anchored text search; equals searches numeric user IDs.");
                string account = request.RequestUri.Query.Contains("bob") ? "bob" : "alice";
                result = new { data = new[] { new Dictionary<string, object> { ["2"] = account == "alice" ? 42 : 43 } } };
            }
            else if (path.Contains("/User/"))
            {
                int id = int.Parse(path.Split('/').Last()); result = new { id, name = id == 42 ? "alice" : "bob", is_active = 1, is_deleted = 0 };
            }
            else if (path.EndsWith("/ITILFollowup") && request.Method == HttpMethod.Post)
            { Comments++; LastContent = body.GetProperty("input").GetProperty("content").GetString()!; result = new { id = Comments }; }
            else if (path.EndsWith("/ITILSolution") && request.Method == HttpMethod.Post)
            { Solutions++; Status[body.GetProperty("input").GetProperty("items_id").GetInt32()] = 5; result = new { id = Solutions }; }
            else
            {
                int id = int.Parse(path.Split('/')[3]);
                if (path.EndsWith("/Ticket_User")) result = Assigned.GetValueOrDefault(id) == 0 ? Array.Empty<object>() : new object[] { new { type = 2, users_id = Assigned[id] } };
                else if (path.EndsWith("/ITILFollowup")) result = new[] { new { content = "<p>Комментарий из GLPI</p>" } };
                else if (request.Method == HttpMethod.Get) result = new { id, status = Status.GetValueOrDefault(id, 1), is_deleted = 0 };
                else
                {
                    var input = body.GetProperty("input");
                    if (input.TryGetProperty("_users_id_assign", out var assigned))
                    {
                        if (FailAssignment) return new(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new[] { "ERROR_RIGHT_MISSING", "Denied" }) };
                        Assigned[id] = assigned.GetInt32(); Assignments++;
                    }
                    if (input.TryGetProperty("status", out var status)) Status[id] = status.GetInt32();
                    result = new { id };
                }
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(result) };
        }
    }
    private static JsonElement Callback(long sender, int ticket, string action, string chatType = "private", long? chat = null) => Json(new
    {
        update_id = 1,
        callback_query = new { id = "callback", from = new { id = sender, is_bot = false }, data = action + ":" + ticket,
            message = new { message_id = 1, chat = new { id = chat ?? sender, type = chatType } } }
    });
    private static JsonElement Message(long sender, string text) => Json(new { update_id = 2, message = new { from = new { id = sender, is_bot = false }, chat = new { id = sender, type = "private" }, text } });
    public static async Task RunAsync()
    {
        Check(TelegramBotHandler.RecentReplies(Json(Array.Empty<object>())) == "Ответов пока нет.", "empty ticket history has an explicit placeholder");
        Check(TelegramBotHandler.RecentReplies(Json(new[] { new { id = 1, content = "<p> </p>", is_private = 0 }, new { id = 2, content = "Private", is_private = 1 } })) == "Ответов пока нет.", "empty and private replies are omitted");
        Check(TelegramBotHandler.RecentReplies(Json(Enumerable.Range(1, 7).Reverse().Select(id => new { id, content = "<p>Reply " + id + "</p>", is_private = 0 }).ToArray())) == "Reply 3\nReply 4\nReply 5\nReply 6\nReply 7", "latest five replies ordered by GLPI ID");
        string root = Path.Combine(Path.GetTempPath(), "helper-telegram-" + Guid.NewGuid().ToString("N")); System.IO.Directory.CreateDirectory(root);
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:SettingsFile"] = Path.Combine(root, "integrations.json"), ["Glpi:SettingsFile"] = Path.Combine(root, "glpi.json"),
                ["Glpi:BaseUrl"] = "http://mock/apirest.php", ["Glpi:UserToken"] = "test-token"
            }).Build();
            var protection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root, "keys")));
            var settings = new IntegrationSettings(config, protection);
            settings.SaveTelegram(new(true, "123:token", "-100888", ManagementEnabled: true, DirectoryLogin: "alice@ad.test", DirectoryPassword: "AD-test-password"));
            Check(!File.ReadAllText(Path.Combine(root, "integrations.json")).Contains("AD-test-password"), "directory password encrypted on disk");
            string view = Json(settings.TelegramView()).ToString(); Check(!view.Contains("AD-test-password") && !view.Contains("123:token"), "settings never expose directory password or bot token");
            settings.SaveTelegram(new(true, "", "-100888", ManagementEnabled: true, DirectoryLogin: "alice@ad.test"));
            Check(settings.Telegram().DirectoryPassword == "AD-test-password", "blank directory password preserves stored credential");
            await using (var ldap = new MockAd())
            {
                settings.SaveAd(new LitleHelperServer.AdUpdate(true, "localhost", ldap.Port, "ad.test", "AD", "DC=ad,DC=test", ldap.Pem));
                var identity = await new TelegramDirectory(settings).FindAsync(123, CancellationToken.None);
                Check(identity.Username == "alice@ad.test", "LDAPS directory mapping handles subordinate referrals");
            }
            using var transport = new Transport(); using var http = new HttpClient(transport);
            var glpi = new GlpiService(http, new GlpiSettingsStore(config, protection), NullLogger<GlpiService>.Instance);
            var telegram = new TelegramClient(http, settings, config);
            var options = new DbContextOptionsBuilder<HelperDb>().UseSqlite("Data Source=" + Path.Combine(root, "test.db") + ";Pooling=False").Options;
            using var db = new HelperDb(options); await db.Database.EnsureCreatedAsync();
            // Exercise upgrading an existing deployment, then verify an idempotent second startup.
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Tickets DROP COLUMN AssignedGlpiUserId");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Tickets DROP COLUMN AssignedUsername");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Tickets DROP COLUMN SyncedAt");
            await TicketManagement.EnsureSchemaAsync(db); await TicketManagement.EnsureSchemaAsync(db);
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Users DROP COLUMN AssistantMachine");
            await AnnouncementService.EnsureSchemaAsync(db); await AnnouncementService.EnsureSchemaAsync(db);
            Check(true,"legacy user schema gains assistant binding without destructive migration");
            var alice = new PanelUser { Username = "alice@ad.test", PasswordHash = "!AD", Role = Roles.User };
            var bob = new PanelUser { Username = "bob@ad.test", PasswordHash = "!AD", Role = Roles.Admin };
            var ticket = new TicketRecord { GlpiId = 100, Description = "Issue" };
            db.Users.AddRange(alice, bob); db.Tickets.Add(ticket); await db.SaveChangesAsync();
            var gate = new TicketManagementGate(); var management = new TicketManagement(db, glpi, gate);
            var handler = new TelegramBotHandler(db, new Directory(), glpi, management, telegram, settings);
            try { await handler.ActorAsync(123, default); throw new Exception("User role accepted"); } catch (UnauthorizedAccessException) { Check(true, "AD mapping alone does not grant management rights"); }
            alice.Role = Roles.Admin; await db.SaveChangesAsync();
            var actor = await handler.ActorAsync(123, default);
            Check(actor.GlpiUserId == 42, "Telegram AD login maps to exact active GLPI user");
            await Branches.EnsureSchemaAsync(db); await Branches.EnsureSchemaAsync(db);
            var branch = new Branch { Name = "Clinic" }; db.Branches.Add(branch); await db.SaveChangesAsync();
            try { await Branches.ValidateTicketAsync(db, null, "12"); throw new Exception("Missing branch accepted"); } catch (ArgumentException) { Check(true, "configured branches require explicit ticket location"); }
            try { await Branches.ValidateTicketAsync(db, branch.Id, ""); throw new Exception("Missing room accepted"); } catch (ArgumentException) { Check(true, "branch ticket requires room"); }
            Check((await Branches.ValidateTicketAsync(db, branch.Id, "12"))!.Name == "Clinic", "active branch and room accepted");
            ticket.BranchId = branch.Id; ticket.BranchName = branch.Name; ticket.Room = "12"; await db.SaveChangesAsync();
            var otherBranch = new Branch { Name = "Other" }; db.Branches.Add(otherBranch); await db.SaveChangesAsync();
            alice.BranchId = otherBranch.Id; await db.SaveChangesAsync();
            await handler.HandleAsync(Callback(123, ticket.Id, "claim", "supergroup", -100888), default);
            Check(transport.Assignments == 0, "administrator without matching branch cannot claim ticket");
            alice.BranchId = branch.Id; await db.SaveChangesAsync(); actor = await handler.ActorAsync(123, default);
            Check(Branches.CanHandle(new PanelUser { Role = Roles.SuperAdmin }, ticket), "super administrator can handle every branch");
            await telegram.SendTicketAsync(ticket, default); Check(transport.HasClaimButton, "new group notification contains accept button");
            await handler.HandleAsync(Callback(123, ticket.Id, "claim", "supergroup", -999), default);
            Check(transport.Assignments == 0, "accept button from another group is rejected");
            await handler.HandleAsync(Callback(123, ticket.Id, "claim", "supergroup", -100888), default);
            Check(transport.Assigned[100] == 42 && ticket.AssignedUsername == alice.Username && ticket.Status == "2", "accept assigns technician and status in GLPI");
            await handler.HandleAsync(Callback(124, ticket.Id, "claim", "supergroup", -100888), default);
            Check(transport.Assigned[100] == 42 && transport.Assignments == 1, "second technician cannot take an assigned ticket");
            await handler.HandleAsync(Callback(124, ticket.Id, "reply"), default); Check(!await db.TelegramReplySessions.AnyAsync(s => s.Id == 124), "other technician cannot start a private reply");
            await handler.HandleAsync(Callback(123, ticket.Id, "reply"), default);
            await handler.HandleAsync(Message(123, "Ответ <script>alert(1)</script>"), default);
            Check(transport.Comments == 1 && transport.LastContent.Contains("alice@ad.test") && transport.LastContent.Contains("&lt;script&gt;"), "reply persisted in GLPI with actor attribution and escaped content");
            await handler.HandleAsync(Message(123, "Ответ повторно"), default); Check(transport.Comments == 1, "consumed reply cannot be accidentally replayed");
            await handler.HandleAsync(Callback(123, ticket.Id, "reply"), default);
            alice.IsActive = false; await db.SaveChangesAsync(); await handler.HandleAsync(Message(123, "Denied"), default); Check(transport.Comments == 1, "disabled panel account loses bot access immediately");
            alice.IsActive = true; await db.SaveChangesAsync();
            await handler.HandleAsync(Message(123, "/cancel"), default); Check(!await db.TelegramReplySessions.AnyAsync(), "cancel clears pending reply");
            await handler.HandleAsync(Callback(123, ticket.Id, "status:invalid"), default);
            await handler.HandleAsync(Callback(123, ticket.Id, "solve"), default); await handler.HandleAsync(Message(123, "Решено"), default);
            Check(transport.Solutions == 1 && ticket.Status == "5", "solution saved in GLPI and resolved status confirmed");
            var close = Callback(123, ticket.Id, "status").ToString().Replace("status:" + ticket.Id + "\"", "status:" + ticket.Id + ":6\"");
            await handler.HandleAsync(JsonDocument.Parse(close).RootElement, default); Check(ticket.Status == "6", "resolved ticket can be closed through bot");
            await handler.HandleAsync(Callback(123, ticket.Id, "reply"), default); Check(!await db.TelegramReplySessions.AnyAsync(), "closed ticket rejects new replies");
            transport.Status[100] = 4; transport.Assigned[100] = 43; await management.SyncAsync(ticket, default);
            Check(ticket.Status == "4" && ticket.AssignedGlpiUserId == 43 && ticket.AssignedUsername == "", "external GLPI status and reassignment replace local cache");
            var race = new TicketRecord { GlpiId = 101 }; db.Tickets.Add(race); await db.SaveChangesAsync();
            async Task<bool> Claim(PanelUser user, int id)
            {
                using var context = new HelperDb(options); var record = await context.Tickets.SingleAsync(t => t.Id == race.Id);
                try { await new TicketManagement(context, glpi, gate).ClaimAsync(record, new(user, id), default); return true; } catch (InvalidOperationException) { return false; }
            }
            bool[] winners = await Task.WhenAll(Claim(alice, 42), Claim(bob, 43)); Check(winners.Count(x => x) == 1, "simultaneous accept calls have exactly one winner");
            var denied = new TicketRecord { GlpiId = 102 }; db.Tickets.Add(denied); await db.SaveChangesAsync(); transport.FailAssignment = true;
            try { await management.ClaimAsync(denied, actor, default); throw new Exception("GLPI error ignored"); } catch (InvalidOperationException) { Check(denied.AssignedGlpiUserId == 0, "GLPI failure never reports a successful assignment"); }
            Check(await db.AuditLogs.AnyAsync(a => a.AdminUsername == "alice@ad.test" && a.CommandType == "telegram_ticket_reply"), "ticket actions audited against AD identity");
            transport.FailAssignment = false;
            var replayTicket = new TicketRecord { GlpiId = 103 }; db.Tickets.Add(replayTicket); await db.SaveChangesAsync(); transport.Assigned[103] = 42;
            await handler.HandleAsync(Callback(123, replayTicket.Id, "reply"), default);
            var state = new TelegramBotState { Id = "test-bot" }; db.TelegramBotStates.Add(state); await db.SaveChangesAsync();
            int before = transport.Comments;
            var repeated = Json(new[] { Message(123, "Exactly once"), Message(123, "Exactly once") });
            await new TelegramBotInbox(db, handler, NullLogger<TelegramBotInbox>.Instance).ProcessAsync(state, repeated, default);
            Check(transport.Comments == before + 1 && state.Offset == 3, "duplicate Telegram update creates one GLPI comment and advances durable cursor");
            using (var restarted = new HelperDb(options))
            {
                var replayHandler = new TelegramBotHandler(restarted, new Directory(), glpi, new TicketManagement(restarted, glpi, gate), telegram, settings);
                await new TelegramBotInbox(restarted, replayHandler, NullLogger<TelegramBotInbox>.Instance).ProcessAsync(await restarted.TelegramBotStates.SingleAsync(), repeated, default);
            }
            Check(transport.Comments == before + 1, "restart preserves deduplication of GLPI side effects");
            Check(!transport.Messages.Any(m => m.Text == "Готово"), "ticket callbacks do not send redundant Done messages");
            Console.WriteLine("ALL TELEGRAM MANAGEMENT CHECKS PASSED");
        }
        finally { System.IO.Directory.Delete(root, true); }
    }
}
