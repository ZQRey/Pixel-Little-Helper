using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace LitleHelperServer;

public class ChatMessage
{
    [System.ComponentModel.DataAnnotations.Schema.NotMapped] public string? SenderName { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped] public List<ChatFileInfo> Attachments { get; set; } = [];
    public long Id { get; set; }
    public int SenderId { get; set; }
    public int RecipientId { get; set; }
    public string Body { get; set; } = "";
    public string ClientId { get; set; } = "";
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
    public bool IsUrgent { get; set; }
    public string? Command { get; set; }
    public string? BroadcastId { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
}
public record ChatSend(int RecipientId, string Body, string ClientId);
public class ChatPresence
{
    private record PresenceEntry(int UserId, bool IsOpen, Action Abort);
    private readonly Dictionary<string, PresenceEntry> connections = new();

    public void Add(string connection, int user, Action abort, bool isOpen = false)
    {
        lock (connections) { connections[connection] = new PresenceEntry(user, isOpen, abort); }
    }
    public void SetMessengerOpen(string connection, bool isOpen)
    {
        lock (connections)
        {
            if (connections.TryGetValue(connection, out var entry))
                connections[connection] = entry with { IsOpen = isOpen };
        }
    }
    public void Remove(string connection)
    {
        lock (connections) { connections.Remove(connection); }
    }
    public void CloseAll()
    {
        Action[] actions;
        lock (connections) actions = connections.Values.Select(v => v.Abort).ToArray();
        foreach (var abort in actions) abort();
    }
    public string GetStatus(int user)
    {
        lock (connections)
        {
            bool hasConnection = false;
            foreach (var entry in connections.Values)
            {
                if (entry.UserId == user)
                {
                    hasConnection = true;
                    if (entry.IsOpen) return "online";
                }
            }
            return hasConnection ? "available" : "offline";
        }
    }
    public bool Online(int user) => GetStatus(user) != "offline";
}
[Authorize(AuthenticationSchemes = "Bearer")]
public class MessengerHub(HelperDb db, PanelSessions sessions, ChatPresence presence, MessengerSettings settings, CallSessionRegistry callRegistry, LiveKitService liveKit) : Hub
{
    public async Task SetMessengerOpen(bool isOpen)
    {
        presence.SetMessengerOpen(Context.ConnectionId, isOpen);
        await Clients.All.SendAsync("ChatChanged");
    }
    public async Task Typing(int peer)
    {
        if(!settings.Value.Enabled)return;
        if(Context.Items.TryGetValue("typing",out var last)&&last is DateTime date&&DateTime.UtcNow-date<TimeSpan.FromSeconds(2))return;
        Context.Items["typing"]=DateTime.UtcNow;var me=await Messenger.UserAsync(db,Context.User!);await ChatExtras.ValidatePeer(db,me.Id,peer);
        var users=peer<0?await db.ChatGroupMembers.Where(m=>m.GroupId==-peer&&m.UserId!=me.Id&&!db.ChatGroupRestrictions.Any(r=>r.GroupId==m.GroupId&&r.UserId==m.UserId&&r.EndsAt>DateTime.UtcNow)).Select(m=>m.UserId).ToListAsync():new List<int>{peer};
        bool isPsychologist = string.Equals(me.Role, Roles.Psychologist, StringComparison.OrdinalIgnoreCase) || me.FullName.Contains("психолог", StringComparison.OrdinalIgnoreCase) || me.Username.Contains("psychologist", StringComparison.OrdinalIgnoreCase) || me.Username.Contains("psiholog", StringComparison.OrdinalIgnoreCase);
        await Clients.Groups(users.Select(u=>"Chat:"+u)).SendAsync("Typing",new {peer=peer<0?peer:me.Id,userId=me.Id,me.FullName,isPsychologist,until=DateTime.UtcNow.AddSeconds(5)});
    }
    // WebRTC Signaling with CallSessionRegistry and Connection-targeted routing
    public async Task StartCall(int targetUserId, bool isVideo, string callId)
    {
        callId = string.IsNullOrWhiteSpace(callId) ? Guid.NewGuid().ToString("N") : callId;
        var me = await Messenger.UserAsync(db, Context.User!);
        await ChatExtras.ValidatePeer(db, me.Id, targetUserId);

        if (callRegistry.IsUserBusy(targetUserId, callId))
        {
            var targetUser = await db.Users.FindAsync(targetUserId);
            string targetName = targetUser?.FullName ?? "Собеседник";
            await Clients.Caller.SendAsync("CallRejected", callId, targetUserId, targetName, "busy");
            return;
        }

        callRegistry.Create(callId, me.Id, me.Username, me.FullName, Context.ConnectionId, targetUserId, isVideo);

        await Clients.Group("Chat:" + targetUserId).SendAsync("IncomingCall", new {
            callId,
            callerId = me.Id,
            callerName = me.FullName,
            callerUsername = me.Username,
            isVideo
        });
    }

    public async Task StartGroupCall(int groupId, bool isVideo, string callId)
    {
        callId = string.IsNullOrWhiteSpace(callId) ? Guid.NewGuid().ToString("N") : callId;
        var me = await Messenger.UserAsync(db, Context.User!);
        var members = await db.ChatGroupMembers
            .Where(m => m.GroupId == groupId && m.UserId != me.Id && db.Users.Any(u => u.Id == m.UserId && u.IsActive))
            .Select(m => m.UserId)
            .ToListAsync();
        var group = await db.ChatGroups.SingleOrDefaultAsync(g => g.Id == groupId);
        string groupName = group?.Name ?? "Групповой звонок";

        callRegistry.Create(callId, me.Id, me.Username, me.FullName, Context.ConnectionId, 0, isVideo, groupId);

        var allMembers = await db.ChatGroupMembers.Where(m => m.GroupId == groupId).Select(m => m.UserId).ToListAsync();
        await Clients.Groups(allMembers.Select(id => "Chat:" + id)).SendAsync("GroupCallStateChanged", new {
            active = true,
            groupId,
            callId,
            isVideo,
            participantCount = 1,
            participants = new[] { new { userId = me.Id, fullName = me.FullName } }
        });

        foreach (var memberId in members)
        {
            await Clients.Group("Chat:" + memberId).SendAsync("IncomingCall", new {
                callId,
                callerId = me.Id,
                callerName = me.FullName,
                groupId,
                groupName,
                participantCount = 1,
                isVideo
            });
        }
    }

    public async Task JoinGroupCall(string callId)
    {
        var me = await Messenger.UserAsync(db, Context.User!);
        var session = callRegistry.Get(callId);
        if (session == null)
        {
            throw new HubException("Групповой звонок не найден или уже завершён.");
        }

        if (session.GroupId != null)
        {
            int groupId = session.GroupId.Value;
            bool isMember = await db.ChatGroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == me.Id);
            if (!isMember)
            {
                throw new HubException("Вы не являетесь участником данной группы.");
            }

            bool isRestricted = await db.ChatGroupRestrictions.AnyAsync(r => r.GroupId == groupId && r.UserId == me.Id && r.EndsAt > DateTime.UtcNow);
            if (isRestricted)
            {
                throw new HubException("Ваш доступ к звонкам в этой группе временно ограничен.");
            }
        }
        else if (session.IsAdHoc || session.InvitedUserIds.ContainsKey(me.Id) || session.Participants.ContainsKey(me.Id))
        {
            // Ad-hoc звонок, пользователь приглашён или уже участник
        }
        else
        {
            throw new HubException("Вы не приглашены в данный звонок.");
        }

        int maxParticipants = liveKit.IsEnabled ? 50 : 4;
        if (!callRegistry.TryJoinGroup(callId, me.Id, me.Username, me.FullName, Context.ConnectionId, maxParticipants, out session, out var error))
        {
            throw new HubException(error ?? "Не удалось присоединиться к групповому звонку.");
        }

        if (session!.Participants.Count > 4 && liveKit.IsEnabled)
        {
            session.Mode = "livekit";
        }

        var others = session!.Participants.Values.Where(p => p.UserId != me.Id).ToList();
        foreach (var p in others)
        {
            if (!string.IsNullOrEmpty(p.ConnectionId))
            {
                await Clients.Client(p.ConnectionId).SendAsync("ParticipantJoined", callId, new {
                    userId = me.Id,
                    fullName = me.FullName,
                    username = me.Username,
                    connectionId = Context.ConnectionId
                });
            }
        }

        await Clients.Caller.SendAsync("GroupCallJoined", callId, new {
            groupId = session.GroupId,
            isVideo = session.IsVideo,
            mode = session.Mode,
            isRecording = session.IsRecording,
            recordingStartedBy = session.RecordingStartedBy,
            participants = session.Participants.Values.Select(p => new {
                userId = p.UserId,
                fullName = p.FullName,
                username = p.Username,
                isCaller = p.UserId == session.CallerUserId,
                handRaised = p.HandRaised
            }).ToList()
        });

        if (session.GroupId != null)
        {
            int groupId = session.GroupId.Value;
            var allMembers = await db.ChatGroupMembers.Where(m => m.GroupId == groupId).Select(m => m.UserId).ToListAsync();
            await Clients.Groups(allMembers.Select(id => "Chat:" + id)).SendAsync("GroupCallStateChanged", new {
                active = true,
                groupId,
                callId,
                isVideo = session.IsVideo,
                mode = session.Mode,
                isRecording = session.IsRecording,
                recordingStartedBy = session.RecordingStartedBy,
                participantCount = session.Participants.Count,
                participants = session.Participants.Values.Select(p => new { userId = p.UserId, fullName = p.FullName }).ToList()
            });
        }
    }

    public async Task LeaveGroupCall(string callId)
    {
        var me = await Messenger.UserAsync(db, Context.User!);
        await LeaveGroupCallInternal(callId, me.Id);
    }

    private async Task LeaveGroupCallInternal(string callId, int userId)
    {
        if (callRegistry.TryLeaveGroup(callId, userId, out var session, out int remainingCount))
        {
            if (session != null)
            {
                foreach (var p in session.Participants.Values)
                {
                    if (!string.IsNullOrEmpty(p.ConnectionId))
                    {
                        await Clients.Client(p.ConnectionId).SendAsync("ParticipantLeft", callId, userId);
                    }
                }

                int groupId = session.GroupId ?? 0;
                bool stillActive = remainingCount > 1;
                if (groupId != 0)
                {
                    var allMembers = await db.ChatGroupMembers.Where(m => m.GroupId == groupId).Select(m => m.UserId).ToListAsync();

                    await Clients.Groups(allMembers.Select(id => "Chat:" + id)).SendAsync("GroupCallStateChanged", new {
                        active = stillActive,
                        groupId,
                        callId,
                        isVideo = session.IsVideo,
                        mode = session.Mode,
                        isRecording = session.IsRecording,
                        recordingStartedBy = session.RecordingStartedBy,
                        participantCount = session.Participants.Count,
                        participants = session.Participants.Values.Select(p => new { userId = p.UserId, fullName = p.FullName }).ToList()
                    });
                }

                if (!stillActive)
                {
                    if (session.Participants.Count == 1)
                    {
                        var lastPerson = session.Participants.Values.First();
                        if (!string.IsNullOrEmpty(lastPerson.ConnectionId))
                        {
                            await Clients.Client(lastPerson.ConnectionId).SendAsync("CallEnded", callId, 0);
                        }
                    }
                    await RecordCallHistoryAsync(session, "completed");
                }
            }
        }
    }

    public async Task SetCallRecording(string callId, bool isRecording)
    {
        var me = await Messenger.UserAsync(db, Context.User!);
        var session = callRegistry.Get(callId);
        if (session == null) return;
        if (session.CallerUserId != me.Id && session.HostUserId != me.Id)
        {
            throw new HubException("Только организатор может управлять записью звонка.");
        }

        session.IsRecording = isRecording;
        session.RecordingStartedBy = isRecording ? me.FullName : null;

        foreach (var p in session.Participants.Values)
        {
            if (!string.IsNullOrEmpty(p.ConnectionId))
            {
                await Clients.Client(p.ConnectionId).SendAsync("CallRecordingStateChanged", callId, isRecording, me.FullName);
            }
        }
    }

    public async Task RaiseHand(string callId, bool raised)
    {
        var me = await Messenger.UserAsync(db, Context.User!);
        var session = callRegistry.Get(callId);
        if (session != null && session.Participants.TryGetValue(me.Id, out var part))
        {
            part.HandRaised = raised;
            foreach (var p in session.Participants.Values)
            {
                if (!string.IsNullOrEmpty(p.ConnectionId))
                {
                    await Clients.Client(p.ConnectionId).SendAsync("ParticipantHandChanged", callId, me.Id, raised);
                }
            }
        }
    }

    public async Task SetScreenSharing(string callId, bool isSharing)
    {
        var me = await Messenger.UserAsync(db, Context.User!);
        var session = callRegistry.Get(callId);
        if (session == null || session.Status == CallSessionStatus.Ended) return;
        if (!session.Participants.ContainsKey(me.Id) && session.CallerUserId != me.Id && session.CalleeUserId != me.Id) return;

        session.ScreenSharingUserId = isSharing ? me.Id : 0;
        session.ScreenSharingUserFullName = isSharing ? me.FullName : null;

        foreach (var p in session.Participants.Values)
        {
            if (!string.IsNullOrEmpty(p.ConnectionId) && p.UserId != me.Id)
            {
                await Clients.Client(p.ConnectionId).SendAsync("ScreenShareChanged", callId, me.Id, isSharing, me.FullName);
            }
        }
    }

    public async Task InviteToCall(string callId, int targetUserId)
    {
        var me = await Messenger.UserAsync(db, Context.User!);
        var session = callRegistry.Get(callId);
        if (session == null || session.Status == CallSessionStatus.Ended)
        {
            throw new HubException("Звонок не найден или уже завершён.");
        }

        if (!session.Participants.ContainsKey(me.Id))
        {
            throw new HubException("Вы не являетесь участником данного звонка.");
        }

        if (targetUserId == me.Id || targetUserId <= 0)
        {
            throw new HubException("Некорректный идентификатор пользователя.");
        }

        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId && u.IsActive);
        if (target == null)
        {
            throw new HubException("Пользователь не найден или неактивен.");
        }

        if (session.Participants.ContainsKey(targetUserId))
        {
            throw new HubException("Пользователь уже участвует в звонке.");
        }

        int maxParticipants = liveKit.IsEnabled ? 50 : 4;
        if (session.Participants.Count >= maxParticipants)
        {
            throw new HubException($"Достигнут максимум участников звонка ({maxParticipants}).");
        }

        bool was1to1 = session.GroupId == null && !session.IsAdHoc;
        session.IsAdHoc = true;
        session.InvitedUserIds[targetUserId] = true;

        if (session.Participants.Count > 4 && liveKit.IsEnabled)
        {
            session.Mode = "livekit";
        }

        if (was1to1)
        {
            var roster = session.Participants.Values.Select(p => new {
                userId = p.UserId,
                fullName = p.FullName,
                username = p.Username,
                connectionId = p.ConnectionId
            }).ToList();

            foreach (var p in session.Participants.Values)
            {
                if (!string.IsNullOrEmpty(p.ConnectionId))
                {
                    await Clients.Client(p.ConnectionId).SendAsync("CallUpgradedToGroup", callId, new {
                        mode = session.Mode,
                        participants = roster
                    });
                }
            }
        }

        string groupTitle = session.GroupId != null
            ? (await db.ChatGroups.Where(g => g.Id == session.GroupId).Select(g => g.Name).FirstOrDefaultAsync() ?? "Группа")
            : $"Групповой звонок ({me.FullName})";

        await Clients.Group("Chat:" + targetUserId).SendAsync("IncomingCall", new {
            callId = session.CallId,
            callerId = me.Id,
            callerName = me.FullName,
            groupId = session.GroupId ?? 0,
            groupName = groupTitle,
            participantCount = session.Participants.Count,
            isVideo = session.IsVideo,
            isAdHoc = true
        });
    }

    public Task<object> GetGroupCallState(int groupId)
    {
        var session = callRegistry.GetActiveGroupCall(groupId);
        if (session != null && (session.Status == CallSessionStatus.Active || session.Status == CallSessionStatus.Ringing))
        {
            return Task.FromResult<object>(new {
                active = true,
                callId = session.CallId,
                isVideo = session.IsVideo,
                mode = session.Mode,
                isRecording = session.IsRecording,
                recordingStartedBy = session.RecordingStartedBy,
                callerUserId = session.CallerUserId,
                callerName = session.CallerFullName,
                participantCount = session.Participants.Count,
                participants = session.Participants.Values.Select(p => new { userId = p.UserId, fullName = p.FullName }).ToList()
            });
        }
        return Task.FromResult<object>(new { active = false, groupId });
    }

    public async Task EndGroupCallForAll(string callId)
    {
        var me = await Messenger.UserAsync(db, Context.User!);
        var session = callRegistry.Get(callId);
        if (session == null || session.GroupId == null) return;
        if (session.CallerUserId != me.Id)
        {
            throw new HubException("Только организатор может завершить звонок для всех.");
        }

        callRegistry.MarkEnded(callId, "ended_by_host", out var endedSession);
        if (endedSession != null)
        {
            foreach (var p in endedSession.Participants.Values)
            {
                if (!string.IsNullOrEmpty(p.ConnectionId))
                {
                    await Clients.Client(p.ConnectionId).SendAsync("CallEnded", callId, me.Id);
                }
            }

            int groupId = endedSession.GroupId ?? 0;
            if (groupId != 0)
            {
                var allMembers = await db.ChatGroupMembers.Where(m => m.GroupId == groupId).Select(m => m.UserId).ToListAsync();
                await Clients.Groups(allMembers.Select(id => "Chat:" + id)).SendAsync("GroupCallStateChanged", new {
                    active = false,
                    groupId,
                    callId
                });
            }
            await RecordCallHistoryAsync(endedSession, "ended_by_host");
        }
    }

    public async Task AcceptCall(string callId, int callerId)
    {
        var me = await Messenger.UserAsync(db, Context.User!);
        callRegistry.MarkAnswered(callId, Context.ConnectionId, me.Username, me.FullName, out var session);

        string? callerConnId = session?.CallerConnectionId;
        if (!string.IsNullOrEmpty(callerConnId))
        {
            await Clients.Client(callerConnId).SendAsync("CallAccepted", callId, me.Id);
        }
        else
        {
            await Clients.Group("Chat:" + callerId).SendAsync("CallAccepted", callId, me.Id);
        }

        await Clients.GroupExcept("Chat:" + me.Id, Context.ConnectionId).SendAsync("CallAnsweredElsewhere", callId);
    }

    public async Task RejectCall(string callId, int callerId, string reason)
    {
        var me = await Messenger.UserAsync(db, Context.User!);
        callRegistry.MarkEnded(callId, reason, out var session);

        string? callerConnId = session?.CallerConnectionId;
        if (!string.IsNullOrEmpty(callerConnId))
        {
            await Clients.Client(callerConnId).SendAsync("CallRejected", callId, me.Id, me.FullName, reason);
        }
        else
        {
            await Clients.Group("Chat:" + callerId).SendAsync("CallRejected", callId, me.Id, me.FullName, reason);
        }

        await Clients.Group("Chat:" + me.Id).SendAsync("CallEnded", callId, me.Id);
        await RecordCallHistoryAsync(session, reason);
    }

    public async Task SendIceCandidate(string callId, int targetUserId, object candidate, string sdpMid, int sdpMLineIndex)
    {
        var me = await Messenger.UserAsync(db, Context.User!);
        var session = callRegistry.Get(callId);
        string? targetConnId = (session != null && session.Participants.TryGetValue(targetUserId, out var part)) ? part.ConnectionId :
                               (session?.CalleeUserId == targetUserId ? session.CalleeConnectionId :
                               (session?.CallerUserId == targetUserId ? session.CallerConnectionId : null));

        if (!string.IsNullOrEmpty(targetConnId))
        {
            await Clients.Client(targetConnId).SendAsync("IceCandidateReceived", callId, me.Id, candidate, sdpMid, sdpMLineIndex);
        }
        else
        {
            await Clients.Group("Chat:" + targetUserId).SendAsync("IceCandidateReceived", callId, me.Id, candidate, sdpMid, sdpMLineIndex);
        }
    }

    public async Task SendSdpOffer(string callId, int targetUserId, object sdp)
    {
        var me = await Messenger.UserAsync(db, Context.User!);
        var session = callRegistry.Get(callId);
        string? targetConnId = (session != null && session.Participants.TryGetValue(targetUserId, out var part)) ? part.ConnectionId :
                               (session?.CalleeUserId == targetUserId ? session.CalleeConnectionId :
                               (session?.CallerUserId == targetUserId ? session.CallerConnectionId : null));

        if (!string.IsNullOrEmpty(targetConnId))
        {
            await Clients.Client(targetConnId).SendAsync("SdpOfferReceived", callId, me.Id, sdp);
        }
        else
        {
            await Clients.Group("Chat:" + targetUserId).SendAsync("SdpOfferReceived", callId, me.Id, sdp);
        }
    }

    public async Task SendSdpAnswer(string callId, int targetUserId, object sdp)
    {
        var me = await Messenger.UserAsync(db, Context.User!);
        var session = callRegistry.Get(callId);
        string? targetConnId = (session != null && session.Participants.TryGetValue(targetUserId, out var part)) ? part.ConnectionId :
                               (session?.CallerUserId == targetUserId ? session.CallerConnectionId :
                               (session?.CalleeUserId == targetUserId ? session.CalleeConnectionId : null));

        if (!string.IsNullOrEmpty(targetConnId))
        {
            await Clients.Client(targetConnId).SendAsync("SdpAnswerReceived", callId, me.Id, sdp);
        }
        else
        {
            await Clients.Group("Chat:" + targetUserId).SendAsync("SdpAnswerReceived", callId, me.Id, sdp);
        }
    }

    public async Task EndCall(string callId, int targetUserId)
    {
        var me = await Messenger.UserAsync(db, Context.User!);
        var session = callRegistry.Get(callId);

        if (session != null && (session.GroupId != null || session.IsAdHoc))
        {
            await LeaveGroupCallInternal(callId, me.Id);
            return;
        }

        callRegistry.MarkEnded(callId, "hangup", out session);

        string? targetConnId = session?.CallerUserId == targetUserId ? session.CallerConnectionId :
                               (session?.CalleeUserId == targetUserId ? session.CalleeConnectionId : null);

        if (!string.IsNullOrEmpty(targetConnId))
        {
            await Clients.Client(targetConnId).SendAsync("CallEnded", callId, me.Id);
        }
        if (targetUserId != 0)
        {
            await Clients.Group("Chat:" + targetUserId).SendAsync("CallEnded", callId, me.Id);
        }
        await Clients.Group("Chat:" + me.Id).SendAsync("CallEnded", callId, me.Id);

        await RecordCallHistoryAsync(session, "hangup");
    }

    public override async Task OnConnectedAsync()
    {
        if (!settings.Value.Enabled) throw new HubException("Мессенджер отключён администратором.");
        var user = await Messenger.UserAsync(db, Context.User!);
        sessions.Add(Context.ConnectionId, user.Id, Context.Abort);
        presence.Add(Context.ConnectionId, user.Id, Context.Abort);
        await Groups.AddToGroupAsync(Context.ConnectionId, "Chat:" + user.Id);
        await Clients.All.SendAsync("ChatChanged");
        await base.OnConnectedAsync();
    }
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        presence.Remove(Context.ConnectionId);
        sessions.Remove(Context.ConnectionId);

        var user = await Messenger.UserAsync(db, Context.User!);
        var activeSessions = callRegistry.GetSessionsForConnection(Context.ConnectionId);
        foreach (var s in activeSessions)
        {
            if (s.GroupId != null || s.IsAdHoc)
            {
                await LeaveGroupCallInternal(s.CallId, user.Id);
                continue;
            }

            callRegistry.MarkEnded(s.CallId, "disconnected", out _);
            int otherUserId = s.CallerConnectionId == Context.ConnectionId ? s.CalleeUserId : s.CallerUserId;
            string? otherConnId = s.CallerConnectionId == Context.ConnectionId ? s.CalleeConnectionId : s.CallerConnectionId;

            if (!string.IsNullOrEmpty(otherConnId))
            {
                await Clients.Client(otherConnId).SendAsync("CallEnded", s.CallId, 0);
            }
            if (otherUserId != 0)
            {
                await Clients.Group("Chat:" + otherUserId).SendAsync("CallEnded", s.CallId, 0);
            }
            await RecordCallHistoryAsync(s, "disconnected");
        }

        await Clients.All.SendAsync("ChatChanged");
        await base.OnDisconnectedAsync(exception);
    }

    private async Task RecordCallHistoryAsync(CallSession? session, string reason)
    {
        if (session == null || session.HistoryRecorded) return;
        session.HistoryRecorded = true;

        try
        {
            int callerId = session.CallerUserId;
            if (callerId == 0) return;

            string clientId = "call_" + session.CallId;
            var existing = await db.ChatMessages.AsNoTracking().FirstOrDefaultAsync(m => m.ClientId == clientId);
            if (existing != null) return;

            if (session.GroupId != null)
            {
                int groupId = session.GroupId.Value;
                var existingGroup = await db.ChatGroupMessages.AsNoTracking().FirstOrDefaultAsync(m => m.ClientId == clientId);
                if (existingGroup != null) return;

                string body;
                if (session.AnsweredAtUtc != null)
                {
                    var dur = (session.EndedAtUtc ?? DateTime.UtcNow) - session.AnsweredAtUtc.Value;
                    if (dur < TimeSpan.Zero) dur = TimeSpan.Zero;
                    string durStr = dur.TotalHours >= 1 ? dur.ToString(@"hh\:mm\:ss") : dur.ToString(@"mm\:ss");
                    body = session.IsVideo ? $"🎥 Групповой видеозвонок, {durStr}" : $"📞 Групповой аудиозвонок, {durStr}";
                }
                else
                {
                    body = session.IsVideo ? "🎥 Пропущенный групповой видеозвонок" : "📞 Пропущенный групповой звонок";
                }

                var groupMsg = new ChatGroupMessage
                {
                    SenderId = callerId,
                    GroupId = groupId,
                    Body = body,
                    ClientId = clientId,
                    SentAt = session.CreatedAtUtc,
                    Command = "call"
                };

                db.ChatGroupMessages.Add(groupMsg);
                await db.SaveChangesAsync();

                var chatMsgView = new ChatMessage
                {
                    Id = groupMsg.Id,
                    SenderId = callerId,
                    RecipientId = -groupId,
                    Body = body,
                    ClientId = clientId,
                    SentAt = session.CreatedAtUtc,
                    Command = "call",
                    SenderName = session.CallerFullName
                };

                var memberIds = await db.ChatGroupMembers.Where(m => m.GroupId == groupId).Select(m => m.UserId).ToListAsync();
                await Clients.Groups(memberIds.Select(id => "Chat:" + id)).SendAsync("ChatMessage", chatMsgView);
                await Clients.Groups(memberIds.Select(id => "Chat:" + id)).SendAsync("ChatChanged");
                return;
            }

            if (session.IsAdHoc)
            {
                var dur = (session.EndedAtUtc ?? DateTime.UtcNow) - (session.AnsweredAtUtc ?? session.CreatedAtUtc);
                if (dur < TimeSpan.Zero) dur = TimeSpan.Zero;
                string durStr = dur.TotalHours >= 1 ? dur.ToString(@"hh\:mm\:ss") : dur.ToString(@"mm\:ss");
                int pCount = Math.Max(session.Participants.Count, session.InvitedUserIds.Count + 1);
                string adHocBody = $"👥 Групповой звонок, {pCount} участников, {durStr}";

                var targetUserIds = session.Participants.Keys
                    .Concat(session.InvitedUserIds.Keys)
                    .Concat(new[] { session.CalleeUserId })
                    .Where(id => id != callerId && id > 0)
                    .Distinct()
                    .ToList();

                foreach (var otherId in targetUserIds)
                {
                    string clientAdHocId = $"{clientId}_{otherId}";
                    var adHocMsg = new ChatMessage
                    {
                        SenderId = callerId,
                        RecipientId = otherId,
                        Body = adHocBody,
                        ClientId = clientAdHocId,
                        SentAt = session.CreatedAtUtc,
                        Command = "call"
                    };
                    db.ChatMessages.Add(adHocMsg);
                }
                await db.SaveChangesAsync();

                foreach (var otherId in targetUserIds)
                {
                    var adHocViewMsg = new ChatMessage
                    {
                        SenderId = callerId,
                        RecipientId = otherId,
                        Body = adHocBody,
                        ClientId = $"{clientId}_{otherId}",
                        SentAt = session.CreatedAtUtc,
                        Command = "call",
                        SenderName = session.CallerFullName
                    };
                    await Clients.Groups("Chat:" + callerId, "Chat:" + otherId).SendAsync("ChatMessage", adHocViewMsg);
                    await Clients.Groups("Chat:" + callerId, "Chat:" + otherId).SendAsync("ChatChanged");
                }
                return;
            }

            int calleeId = session.CalleeUserId;
            if (calleeId == 0 || callerId == calleeId) return;

            string directBody;
            if (session.AnsweredAtUtc != null)
            {
                var dur = (session.EndedAtUtc ?? DateTime.UtcNow) - session.AnsweredAtUtc.Value;
                if (dur < TimeSpan.Zero) dur = TimeSpan.Zero;
                string durStr = dur.TotalHours >= 1 ? dur.ToString(@"hh\:mm\:ss") : dur.ToString(@"mm\:ss");
                directBody = session.IsVideo ? $"🎥 Видеозвонок, {durStr}" : $"📞 Аудиозвонок, {durStr}";
            }
            else
            {
                directBody = session.IsVideo ? "🎥 Пропущенный видеозвонок" : "📞 Пропущенный звонок";
            }

            var msg = new ChatMessage
            {
                SenderId = callerId,
                RecipientId = calleeId,
                Body = directBody,
                ClientId = clientId,
                SentAt = session.CreatedAtUtc,
                Command = "call"
            };

            db.ChatMessages.Add(msg);
            await db.SaveChangesAsync();

            await Clients.Groups("Chat:" + callerId, "Chat:" + calleeId).SendAsync("ChatMessage", msg);
            await Clients.Groups("Chat:" + callerId, "Chat:" + calleeId).SendAsync("ChatChanged");
        }
        catch
        {
            // Ignore duplicate or concurrent save errors
        }
    }
}
public static class Messenger
{
    public static async Task<PanelUser> UserAsync(HelperDb db, ClaimsPrincipal principal)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == principal.Identity!.Name);
        if (user == null || !user.IsActive || user.AuthSource != "AD") throw new UnauthorizedAccessException("Для мессенджера войдите через AD.");
        return user;
    }
    public static async Task EnsureSchemaAsync(HelperDb db)
    {
        bool pg = db.Database.ProviderName!.Contains("Npgsql");
        if (pg) await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"AdObjectId\" TEXT NOT NULL DEFAULT ''");
        else
        {
            var connection = db.Database.GetDbConnection(); bool open = connection.State == System.Data.ConnectionState.Open;
            if (!open) await connection.OpenAsync();
            using var command = connection.CreateCommand(); command.CommandText = "PRAGMA table_info('Users')";
            bool exists = false;
            using (var reader = await command.ExecuteReaderAsync()) while (await reader.ReadAsync()) if (reader.GetString(1) == "AdObjectId") exists = true;
            if (!exists) await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN \"AdObjectId\" TEXT NOT NULL DEFAULT ''");
            if (!open) await connection.CloseAsync();
        }
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Users_AdObjectId\" ON \"Users\" (\"AdObjectId\") WHERE \"AdObjectId\" <> ''");
        string id = pg ? "BIGINT GENERATED BY DEFAULT AS IDENTITY" : "INTEGER";
        string date = pg ? "timestamp with time zone" : "TEXT";
        string sql = $"CREATE TABLE IF NOT EXISTS \"ChatMessages\" (\"Id\" {id} PRIMARY KEY, \"SenderId\" INTEGER NOT NULL REFERENCES \"Users\"(\"Id\"), \"RecipientId\" INTEGER NOT NULL REFERENCES \"Users\"(\"Id\"), \"Body\" TEXT NOT NULL, \"ClientId\" TEXT NOT NULL, \"SentAt\" {date} NOT NULL, \"ReadAt\" {date} NULL)";
        await db.Database.ExecuteSqlRawAsync(sql); // Types above are fixed literals, never request data.
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_ChatMessages_SenderId_ClientId\" ON \"ChatMessages\" (\"SenderId\", \"ClientId\")");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_ChatMessages_RecipientId_Id\" ON \"ChatMessages\" (\"RecipientId\", \"Id\")");
        await ChatFiles.EnsureSchemaAsync(db);
        await ChatGroups.EnsureSchemaAsync(db);
        await ChatBroadcasts.EnsureSchemaAsync(db); await ChatExtras.EnsureSchemaAsync(db); await ChatPolls.EnsureSchemaAsync(db);
    }
    internal static string Token(PanelUser user, IConfiguration config)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Username), new Claim(ClaimTypes.Role, Roles.User), new Claim("version", user.SecurityVersion.ToString()), new Claim("messenger", "1") };
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"], claims, expires: DateTime.UtcNow.AddHours(8), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:SigningKey"]!)), SecurityAlgorithms.HmacSha256)));
    }
    public static async Task<PanelUser> ResolveAdUserAsync(AdIdentity identity, HelperDb db, CancellationToken cancellation)
    {
        var user = identity.ObjectId.Length == 0 ? null : await db.Users.SingleOrDefaultAsync(u => u.AdObjectId == identity.ObjectId, cancellation);
        user ??= await db.Users.SingleOrDefaultAsync(u => u.Username == identity.Username, cancellation);
        if (user != null && (user.AuthSource != "AD" || !user.IsActive || user.AdObjectId.Length > 0 && user.AdObjectId != identity.ObjectId)) throw new UnauthorizedAccessException("Учётная запись отключена или заменена в AD.");
        if (user == null)
        {
            user = new() { Username = identity.Username, FullName = identity.FullName, PasswordHash = "!AD", Role = Roles.User, AdObjectId = identity.ObjectId }; db.Users.Add(user);
            try { await db.SaveChangesAsync(cancellation); }
            catch (DbUpdateException)
            {
                db.Entry(user).State = EntityState.Detached;
                user = await db.Users.SingleOrDefaultAsync(u => u.Username == identity.Username, cancellation);
                if (user == null || !user.IsActive || user.AuthSource != "AD" || user.AdObjectId.Length > 0 && user.AdObjectId != identity.ObjectId) throw new UnauthorizedAccessException("Учётная запись недоступна.");
            }
        }
        if (user.Username != identity.Username && await db.Users.AnyAsync(u => u.Id != user.Id && u.Username == identity.Username, cancellation)) throw new UnauthorizedAccessException("Новое имя AD занято другой учётной записью в панели.");
        user.Username = identity.Username; user.FullName = identity.FullName; user.AdObjectId = identity.ObjectId;

        if (user.BranchId == null && user.Role != Roles.SuperAdmin && user.Role != Roles.Admin)
        {
            string norm = Security.NormalizeAccount(user.Username);
            var comps = await db.Computers.Where(c => c.BranchId != null && c.CurrentUser != null).ToListAsync(cancellation);
            var comp = comps.FirstOrDefault(c => Security.NormalizeAccount(c.CurrentUser) == norm);
            if (comp?.BranchId != null)
            {
                user.BranchId = comp.BranchId;
                if (string.IsNullOrWhiteSpace(user.Room) && !string.IsNullOrWhiteSpace(comp.Room))
                    user.Room = comp.Room;
            }
        }

        await db.SaveChangesAsync(cancellation); return user;
    }
    public static void MapMessenger(this WebApplication app)
    {
        MessengerWeb.Map(app);
        app.MapGet("/api/messenger/windows", async (HttpContext http, IMessengerKerberos kerberos, IMessengerWindowsDirectory directory, HelperDb db, IConfiguration config, MessengerSettings settings, CancellationToken cancellation) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (!http.Request.IsHttps) return Results.BadRequest(new { error = "Автовход доступен только через HTTPS." });
            if (!settings.Value.Enabled) return Results.Json(new { error = "Мессенджер отключён администратором." }, statusCode: 403);
            string header = http.Request.Headers.Authorization.ToString();
            IResult Challenge()
            { http.Response.Headers.WWWAuthenticate = "Negotiate"; return Results.Unauthorized(); }
            if (!header.StartsWith("Negotiate ", StringComparison.OrdinalIgnoreCase) || header.Length > 65536) return Challenge();
            KerberosIdentity verified;
            try { verified = kerberos.Authenticate(header[10..]); }
            catch (Exception ex) when (ex is System.Security.Authentication.AuthenticationException or FormatException or ArgumentException) { return Challenge(); }
            AdIdentity identity;
            PanelUser user;
            try { identity = await directory.FindAsync(verified.Name, cancellation); user = await ResolveAdUserAsync(identity, db, cancellation); }
            catch (UnauthorizedAccessException) { return Results.Json(new { error = "Учётная запись Windows отключена или недоступна в AD." }, statusCode: 403); }
            if (!string.IsNullOrEmpty(verified.ResponseToken)) http.Response.Headers.WWWAuthenticate = "Negotiate " + verified.ResponseToken;
            return Results.Ok(new { token = Token(user, config), user.Id, user.FullName });
        }).RequireRateLimiting("login");
        app.MapGet("/api/settings/messenger", async (MessengerSettings settings, HelperDb db) => new { settings.Value.Enabled, settings.Value.RetentionDays, settings.Value.UnreadReminderMinutes, storedMessages = await db.ChatMessages.CountAsync() + await db.ChatGroupMessages.CountAsync() }).RequireAuthorization("settings.manage");
        app.MapPut("/api/settings/messenger", async (ChatOptions request, MessengerSettings settings, ChatPresence presence, IHubContext<MessengerHub> hub) =>
        {
            settings.Save(request);
            if (!request.Enabled) presence.CloseAll();
            await hub.Clients.All.SendAsync("MessengerSettingsUpdated", settings.Value.UnreadReminderMinutes);
            return Results.Ok(settings.Value);
        }).RequireAuthorization("settings.manage");
        app.MapHub<MessengerHub>("/messengerHub", o => o.CloseOnAuthenticationExpiration = true);
        app.MapPost("/api/messenger/login", async (LoginRequest request, HttpRequest http, IAdAuthentication ad, HelperDb db, IConfiguration config, MessengerSettings settings, CancellationToken cancellation) =>
        {
            if (!settings.Value.Enabled) return Results.BadRequest(new { error = "Мессенджер отключён администратором." });
            if (!http.IsHttps && http.Headers["X-Forwarded-Proto"] != "https") return Results.BadRequest(new { error = "Вход в мессенджер доступен только через HTTPS." });
            AdIdentity identity;
            try { identity = await ad.AuthenticateAsync(request, cancellation); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
            PanelUser user;
            try { user = await ResolveAdUserAsync(identity, db, cancellation); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
            return Results.Ok(new { token = Token(user, config), user.Id, user.FullName });
        }).RequireRateLimiting("login");
        var api = app.MapGroup("/api/messenger").RequireAuthorization("Panel");
        ChatGroups.Map(api);
        ChatFiles.Map(api);
        ChatBroadcasts.Map(api);
        ChatExtras.Map(api);
        ChatPolls.Map(api);
        api.AddEndpointFilter(async (context, next) => context.HttpContext.RequestServices.GetRequiredService<MessengerSettings>().Value.Enabled ? await next(context) : Results.Json(new { error = "Мессенджер отключён администратором." }, statusCode: 403));
        api.MapGet("/me", async (HelperDb db, ClaimsPrincipal p, MessengerSettings settings) => { var u = await UserAsync(db, p); return new { u.Id, u.FullName, unreadReminderMinutes = settings.Value.UnreadReminderMinutes }; });
        api.MapGet("/config", (MessengerSettings settings) => new { settings.Value.Enabled, settings.Value.RetentionDays, settings.Value.UnreadReminderMinutes });
        api.MapGet("/users", async (HelperDb db, ClaimsPrincipal p, ChatPresence presence) =>
        {
            var me = await UserAsync(db, p);
            var users = await db.Users.AsNoTracking().Where(u => u.Id != me.Id && u.PasswordHash == "!AD" && (u.IsActive || db.ChatMessages.Any(m => (m.SenderId == me.Id && m.RecipientId == u.Id || m.SenderId == u.Id && m.RecipientId == me.Id) && (m.Body != "" || db.ChatFiles.Any(f => f.MessageId == m.Id)))))
                .OrderBy(u => u.FullName).Select(u => new {
                    u.Id,
                    u.FullName,
                    u.Username,
                    u.IsActive,
                    branch = db.Branches.Where(b => b.Id == u.BranchId).Select(b => b.Name).FirstOrDefault(),
                    unread = db.ChatMessages.Count(m => m.SenderId == u.Id && m.RecipientId == me.Id && m.ReadAt == null && (m.Body != "" || db.ChatFiles.Any(f => f.MessageId == m.Id))),
                    lastId = db.ChatMessages.Where(m => (m.SenderId == me.Id && m.RecipientId == u.Id || m.SenderId == u.Id && m.RecipientId == me.Id) && (m.Body != "" || db.ChatFiles.Any(f => f.MessageId == m.Id))).Select(m => (long?)m.Id).Max(),
                    lastAt = db.ChatMessages.Where(m => (m.SenderId == me.Id && m.RecipientId == u.Id || m.SenderId == u.Id && m.RecipientId == me.Id) && (m.Body != "" || db.ChatFiles.Any(f => f.MessageId == m.Id))).OrderByDescending(m => m.Id).Select(m => (DateTime?)m.SentAt).FirstOrDefault(),
                    lastText = db.ChatMessages.Where(m => (m.SenderId == me.Id && m.RecipientId == u.Id || m.SenderId == u.Id && m.RecipientId == me.Id) && (m.Body != "" || db.ChatFiles.Any(f => f.MessageId == m.Id))).OrderByDescending(m => m.Id).Select(m => m.Body).FirstOrDefault()
                }).ToListAsync();
            return users.Select(u =>
            {
                var status = presence.GetStatus(u.Id);
                return new ChatListItem(u.Id, u.FullName, u.Username, u.IsActive, u.branch, u.unread, u.lastId, status != "offline", u.lastAt, u.lastText, Status: status);
            }).Concat(await ChatGroups.ListAsync(db, me.Id));
        });
        api.MapGet("/history/{peer:int}", async (int peer, long? before, HelperDb db, ClaimsPrincipal p) =>
        {
            var me = await UserAsync(db, p);
            if (peer < 0) return await ChatGroups.HistoryAsync(ChatGroups.IdFromPeer(peer), before, db, me.Id);
            var rows = (await db.ChatMessages.AsNoTracking().Where(m => (m.SenderId == me.Id && m.RecipientId == peer || m.SenderId == peer && m.RecipientId == me.Id) && (before == null || m.Id < before)).OrderByDescending(m => m.Id).Take(50).ToListAsync()).OrderBy(m => m.Id).ToList();
            await ChatFiles.PopulateAsync(db, rows); return Results.Ok(rows);
        });
        api.MapPost("/send", async (ChatSend request, HelperDb db, ClaimsPrincipal p, IHubContext<MessengerHub> hub, ChatGroupGate gate) =>
        {
            var me = await UserAsync(db, p);
            if (request.RecipientId < 0) return await ChatGroups.SendAsync(request, db, me.Id, hub, gate);
            if (request.RecipientId == me.Id || !Guid.TryParse(request.ClientId, out _) || string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 4000 || request.Body.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t')) throw new ArgumentException("Сообщение: от 1 до 4000 символов, выберите получателя.");
            var recipient = await db.Users.SingleOrDefaultAsync(u => u.Id == request.RecipientId && u.IsActive && u.PasswordHash == "!AD") ?? throw new ArgumentException("Получатель недоступен.");
            var content = ChatCommands.Parse(request.Body);
            string clientId = Guid.Parse(request.ClientId).ToString("N");
            var message = await db.ChatMessages.SingleOrDefaultAsync(m => m.SenderId == me.Id && m.ClientId == clientId);
            if (message != null)
            {
                if (message.RecipientId != request.RecipientId || message.Body != content.Body || message.IsUrgent != content.Urgent || message.Command != content.Command) throw new ArgumentException("Идентификатор сообщения уже использован.");
                return Results.Ok(message);
            }
            message = new() { SenderId = me.Id, RecipientId = recipient.Id, Body = content.Body, IsUrgent = content.Urgent, Command = content.Command, ClientId = clientId };
            db.ChatMessages.Add(message);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                db.Entry(message).State = EntityState.Detached;
                message = await db.ChatMessages.SingleOrDefaultAsync(m => m.SenderId == me.Id && m.ClientId == clientId);
                if (message == null) throw;
                if (message.RecipientId != request.RecipientId || message.Body != content.Body || message.IsUrgent != content.Urgent || message.Command != content.Command) throw new ArgumentException("Идентификатор сообщения уже использован.");
                return Results.Ok(message);
            }
            await hub.Clients.Groups("Chat:" + recipient.Id, "Chat:" + me.Id).SendAsync("ChatMessage", message);
            return Results.Ok(message);
        });
        api.MapPost("/read/{peer:int}/{through:long}", async (int peer, long through, HelperDb db, ClaimsPrincipal p, IHubContext<MessengerHub> hub) =>
        {
            var me = await UserAsync(db, p);
            if (peer < 0) return await ChatGroups.ReadAsync(ChatGroups.IdFromPeer(peer), through, db, me.Id, hub);
            await db.ChatMessages.Where(m => m.SenderId == peer && m.RecipientId == me.Id && m.Id <= through && m.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadAt, DateTime.UtcNow));
            await hub.Clients.Groups("Chat:" + peer, "Chat:" + me.Id).SendAsync("ChatChanged"); return Results.Ok();
        });
        api.MapGet("/webrtc/config", (IConfiguration config) =>
        {
            var stun = config["WebRTC:StunServer"] ?? "stun:172.16.16.61:3478";
            var iceServers = new List<object>
            {
                new { urls = new[] { stun, "stun:helper.gp1.loc:3478", "stun:helper:3478", "stun:stun.l.google.com:19302" } }
            };
            string? turn = config["WebRTC:TurnServer"] ?? "turn:172.16.16.61:3478";
            if (!string.IsNullOrWhiteSpace(turn))
            {
                iceServers.Add(new {
                    urls = new[] { turn, "turn:helper.gp1.loc:3478", "turn:helper:3478" },
                    username = config["WebRTC:TurnUser"] ?? "helper",
                    credential = config["WebRTC:TurnPassword"] ?? "helperpass"
                });
            }
            return Results.Ok(new { iceServers });
        });
        api.MapGet("/groups/{groupId:int}/call-state", (int groupId, CallSessionRegistry callRegistry) =>
        {
            var session = callRegistry.GetActiveGroupCall(groupId);
            if (session != null && (session.Status == CallSessionStatus.Active || session.Status == CallSessionStatus.Ringing))
            {
                return Results.Ok(new {
                    active = true,
                    callId = session.CallId,
                    isVideo = session.IsVideo,
                    mode = session.Mode,
                    isRecording = session.IsRecording,
                    recordingStartedBy = session.RecordingStartedBy,
                    callerUserId = session.CallerUserId,
                    callerName = session.CallerFullName,
                    participantCount = session.Participants.Count,
                    participants = session.Participants.Values.Select(p => new { userId = p.UserId, fullName = p.FullName }).ToList()
                });
            }
            return Results.Ok(new { active = false, groupId });
        });
        api.MapGet("/calls/{callId}/invite-candidates", async (string callId, string? q, HelperDb db, ClaimsPrincipal p, CallSessionRegistry callRegistry) =>
        {
            var me = await UserAsync(db, p);
            var session = callRegistry.Get(callId);
            if (session == null || session.Status == CallSessionStatus.Ended)
            {
                return Results.NotFound(new { error = "Звонок не найден или завершён." });
            }

            if (!session.Participants.ContainsKey(me.Id) && session.CallerUserId != me.Id && session.CalleeUserId != me.Id)
            {
                return Results.StatusCode(403);
            }

            var activeIds = session.Participants.Keys.ToHashSet();
            activeIds.Add(me.Id);

            var query = db.Users.AsNoTracking().Where(u => u.IsActive && !activeIds.Contains(u.Id));
            if (!string.IsNullOrWhiteSpace(q))
            {
                string term = q.Trim().ToLower();
                query = query.Where(u => u.FullName.ToLower().Contains(term) || u.Username.ToLower().Contains(term));
            }

            var candidates = await query
                .OrderBy(u => u.FullName)
                .Take(25)
                .Select(u => new { u.Id, u.FullName, u.Username })
                .ToListAsync();

            return Results.Ok(candidates);
        });
        api.MapPost("/calls/{callId}/token", async (string callId, LiveKitService liveKit, HelperDb db, ClaimsPrincipal p, CallSessionRegistry callRegistry) =>
        {
            var me = await UserAsync(db, p);
            if (!liveKit.IsEnabled) return Results.BadRequest(new { error = "LiveKit SFU не настроен на сервере." });

            bool isHost = false;
            var session = callRegistry.Get(callId);
            if (session != null)
            {
                if (session.GroupId != null)
                {
                    int gId = session.GroupId.Value;
                    var isMember = await db.ChatGroupMembers.AnyAsync(m => m.GroupId == gId && m.UserId == me.Id);
                    if (!isMember) return Results.StatusCode(403);
                    var isBanned = await db.ChatGroupRestrictions.AnyAsync(r => r.GroupId == gId && r.UserId == me.Id && r.EndsAt > DateTime.UtcNow);
                    if (isBanned) return Results.StatusCode(403);
                    isHost = session.CallerUserId == me.Id || session.HostUserId == me.Id;
                }
                else if (session.IsAdHoc)
                {
                    bool isAllowed = session.Participants.ContainsKey(me.Id) || session.InvitedUserIds.ContainsKey(me.Id);
                    if (!isAllowed) return Results.StatusCode(403);
                    isHost = session.CallerUserId == me.Id || session.HostUserId == me.Id;
                }
                else
                {
                    if (session.CallerUserId != me.Id && session.CalleeUserId != me.Id) return Results.StatusCode(403);
                    isHost = session.CallerUserId == me.Id;
                }
            }
            else if (callId.StartsWith("call_grp_") || callId.StartsWith("group_"))
            {
                int gId = 0;
                if (callId.StartsWith("group_") && int.TryParse(callId[6..], out int parsedGId)) gId = parsedGId;
                if (gId != 0)
                {
                    var isMember = await db.ChatGroupMembers.AnyAsync(m => m.GroupId == gId && m.UserId == me.Id);
                    if (!isMember) return Results.StatusCode(403);
                    var group = await db.ChatGroups.FirstOrDefaultAsync(g => g.Id == gId);
                    isHost = group?.OwnerId == me.Id;
                }
            }

            var token = liveKit.GenerateToken(callId, me, isHost);
            return Results.Ok(new { token, url = liveKit.Url, callId, mode = "livekit" });
        });
        api.MapPost("/calls/{callId}/recording/start", async (string callId, HelperDb db, ClaimsPrincipal p, CallSessionRegistry callRegistry, IHubContext<MessengerHub> hub) =>
        {
            var me = await UserAsync(db, p);
            var session = callRegistry.Get(callId);
            if (session == null) return Results.NotFound(new { error = "Звонок не найден" });
            if (session.CallerUserId != me.Id && session.HostUserId != me.Id) return Results.StatusCode(403);

            session.IsRecording = true;
            session.RecordingStartedBy = me.FullName;
            foreach (var part in session.Participants.Values)
            {
                if (!string.IsNullOrEmpty(part.ConnectionId))
                {
                    await hub.Clients.Client(part.ConnectionId).SendAsync("CallRecordingStateChanged", callId, true, me.FullName);
                }
            }
            return Results.Ok(new { status = "recording_started", callId, startedBy = me.FullName });
        });
        api.MapPost("/calls/{callId}/recording/stop", async (string callId, HelperDb db, ClaimsPrincipal p, CallSessionRegistry callRegistry, IHubContext<MessengerHub> hub) =>
        {
            var me = await UserAsync(db, p);
            var session = callRegistry.Get(callId);
            if (session == null) return Results.NotFound(new { error = "Звонок не найден" });
            if (session.CallerUserId != me.Id && session.HostUserId != me.Id) return Results.StatusCode(403);

            session.IsRecording = false;
            session.RecordingStartedBy = null;
            foreach (var part in session.Participants.Values)
            {
                if (!string.IsNullOrEmpty(part.ConnectionId))
                {
                    await hub.Clients.Client(part.ConnectionId).SendAsync("CallRecordingStateChanged", callId, false, me.FullName);
                }
            }
            return Results.Ok(new { status = "recording_stopped", callId });
        });
    }
}
