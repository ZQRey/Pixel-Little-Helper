using System.Collections.Concurrent;

namespace LitleHelperServer;
public class PanelSessions
{
    private readonly ConcurrentDictionary<string, (int UserId, Action Abort)> connections = new();
    public void Add(string connection, int userId, Action abort) => connections[connection] = (userId, abort);
    public void Remove(string connection) => connections.TryRemove(connection, out _);
    public void Revoke(int userId)
    {
        foreach (var pair in connections.Where(p => p.Value.UserId == userId))
            if (connections.TryRemove(pair.Key, out var value)) value.Abort();
    }
}
