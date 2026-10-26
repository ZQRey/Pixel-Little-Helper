namespace LitleHelperServer;

using System.Collections.Concurrent;

public enum CallSessionStatus
{
    Ringing,
    Active,
    Ended,
    Rejected,
    Missed,
    Busy
}

public class CallParticipant
{
    public int UserId { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string ConnectionId { get; set; } = "";
    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
    public bool HandRaised { get; set; }
}

public class CallSession
{
    public string CallId { get; set; } = "";
    public int CallerUserId { get; set; }
    public string CallerUsername { get; set; } = "";
    public string CallerFullName { get; set; } = "";
    public string CallerConnectionId { get; set; } = "";
    public int CalleeUserId { get; set; }
    public string? CalleeConnectionId { get; set; }
    public bool IsVideo { get; set; }
    public int? GroupId { get; set; }
    public CallSessionStatus Status { get; set; } = CallSessionStatus.Ringing;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AnsweredAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public string? EndReason { get; set; }
    public bool HistoryRecorded { get; set; }
    public string Mode { get; set; } = "mesh";
    public bool IsRecording { get; set; }
    public string? RecordingStartedBy { get; set; }
    public int HostUserId => CallerUserId;
    public bool IsAdHoc { get; set; }
    public int ScreenSharingUserId { get; set; }
    public string? ScreenSharingUserFullName { get; set; }
    public ConcurrentDictionary<int, bool> InvitedUserIds { get; } = new();

    /// <summary>
    /// Ростер активных участников звонка (актуально как для 1-на-1, так и для группового mesh).
    /// </summary>
    public ConcurrentDictionary<int, CallParticipant> Participants { get; } = new();

    public TimeSpan Duration => (EndedAtUtc ?? DateTime.UtcNow) - (AnsweredAtUtc ?? CreatedAtUtc);
}

public class CallSessionRegistry
{
    private readonly ConcurrentDictionary<string, CallSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public CallSession? Get(string callId)
    {
        if (string.IsNullOrWhiteSpace(callId)) return null;
        _sessions.TryGetValue(callId, out var session);
        return session;
    }

    public CallSession Create(string callId, int callerUserId, string callerUsername, string callerFullName, string callerConnectionId, int calleeUserId, bool isVideo, int? groupId = null)
    {
        var session = new CallSession
        {
            CallId = callId,
            CallerUserId = callerUserId,
            CallerUsername = callerUsername,
            CallerFullName = callerFullName,
            CallerConnectionId = callerConnectionId,
            CalleeUserId = calleeUserId,
            IsVideo = isVideo,
            GroupId = groupId,
            Status = groupId != null ? CallSessionStatus.Active : CallSessionStatus.Ringing,
            CreatedAtUtc = DateTime.UtcNow,
            AnsweredAtUtc = groupId != null ? DateTime.UtcNow : null
        };

        if (callerUserId > 0)
        {
            session.Participants[callerUserId] = new CallParticipant
            {
                UserId = callerUserId,
                Username = callerUsername,
                FullName = callerFullName,
                ConnectionId = callerConnectionId,
                JoinedAtUtc = DateTime.UtcNow
            };
        }

        _sessions[callId] = session;
        return session;
    }

    public bool MarkAnswered(string callId, string calleeConnectionId, string? calleeUsername, string? calleeFullName, out CallSession? session)
    {
        if (_sessions.TryGetValue(callId, out session))
        {
            session.CalleeConnectionId = calleeConnectionId;
            session.Status = CallSessionStatus.Active;
            session.AnsweredAtUtc = DateTime.UtcNow;

            if (session.CalleeUserId > 0)
            {
                session.Participants[session.CalleeUserId] = new CallParticipant
                {
                    UserId = session.CalleeUserId,
                    Username = calleeUsername ?? "",
                    FullName = calleeFullName ?? "",
                    ConnectionId = calleeConnectionId,
                    JoinedAtUtc = DateTime.UtcNow
                };
            }

            return true;
        }
        return false;
    }

    public bool MarkAnswered(string callId, string calleeConnectionId, out CallSession? session)
        => MarkAnswered(callId, calleeConnectionId, null, null, out session);

    public bool TryJoinGroup(string callId, int userId, string username, string fullName, string connectionId, int maxParticipants, out CallSession? session, out string? error)
    {
        error = null;
        if (!_sessions.TryGetValue(callId, out session))
        {
            error = "Звонок не найден или уже завершён.";
            return false;
        }

        if (session.GroupId == null && !session.IsAdHoc && !session.InvitedUserIds.ContainsKey(userId) && !session.Participants.ContainsKey(userId))
        {
            error = "Данный звонок не является групповым.";
            return false;
        }

        if (session.Status == CallSessionStatus.Ended)
        {
            error = "Звонок уже завершён.";
            return false;
        }

        if (!session.Participants.ContainsKey(userId) && session.Participants.Count >= maxParticipants)
        {
            error = $"В групповом звонке достигнут лимит участников ({maxParticipants}).";
            return false;
        }

        session.Participants[userId] = new CallParticipant
        {
            UserId = userId,
            Username = username,
            FullName = fullName,
            ConnectionId = connectionId,
            JoinedAtUtc = DateTime.UtcNow
        };
        session.Status = CallSessionStatus.Active;
        if (session.AnsweredAtUtc == null) session.AnsweredAtUtc = DateTime.UtcNow;

        return true;
    }

    public bool TryLeaveGroup(string callId, int userId, out CallSession? session, out int remainingCount)
    {
        remainingCount = 0;
        if (_sessions.TryGetValue(callId, out session))
        {
            session.Participants.TryRemove(userId, out _);
            remainingCount = session.Participants.Count;

            if (remainingCount <= 1 && (session.GroupId != null || session.IsAdHoc))
            {
                session.Status = CallSessionStatus.Ended;
                session.EndedAtUtc = DateTime.UtcNow;
                _sessions.TryRemove(callId, out _);
            }
            return true;
        }
        return false;
    }

    public CallSession? GetActiveGroupCall(int groupId)
    {
        return _sessions.Values.FirstOrDefault(s =>
            s.GroupId == groupId &&
            (s.Status == CallSessionStatus.Active || s.Status == CallSessionStatus.Ringing)
        );
    }

    public bool MarkEnded(string callId, string? reason, out CallSession? session)
    {
        if (_sessions.TryGetValue(callId, out session))
        {
            session.EndedAtUtc = DateTime.UtcNow;
            session.EndReason = reason;
            if (session.Status == CallSessionStatus.Ringing)
            {
                session.Status = reason == "busy" ? CallSessionStatus.Busy :
                                 (reason == "rejected" ? CallSessionStatus.Rejected : CallSessionStatus.Missed);
            }
            else
            {
                session.Status = CallSessionStatus.Ended;
            }
            session.Participants.Clear();
            _sessions.TryRemove(callId, out _);
            return true;
        }
        return false;
    }

    public bool IsUserBusy(int userId, string? excludeCallId = null)
    {
        return _sessions.Values.Any(s =>
            (s.CallerUserId == userId || s.CalleeUserId == userId || s.Participants.ContainsKey(userId)) &&
            (excludeCallId == null || !string.Equals(s.CallId, excludeCallId, StringComparison.OrdinalIgnoreCase)) &&
            (s.Status == CallSessionStatus.Active || s.Status == CallSessionStatus.Ringing)
        );
    }

    public List<CallSession> GetSessionsForConnection(string connectionId)
    {
        return _sessions.Values.Where(s =>
            (s.CallerConnectionId == connectionId || s.CalleeConnectionId == connectionId || s.Participants.Values.Any(p => p.ConnectionId == connectionId)) &&
            (s.Status == CallSessionStatus.Ringing || s.Status == CallSessionStatus.Active)
        ).ToList();
    }
}
