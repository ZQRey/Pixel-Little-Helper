using System.Collections.Concurrent;

namespace LitleHelperServer;

// Polling the native queue is an explicit capability signal from existing clients.
// Keep it on the connection, never infer it from a machine name or user identity.
public static class SurveyClients
{
    private static readonly ConcurrentDictionary<string, bool> native = new();
    public static void Register(string connection, bool queue = true) => native.AddOrUpdate(connection,queue,(_,known)=>known || queue);
    public static bool Supports(string? connection) => connection != null && native.ContainsKey(connection);
    public static bool SupportsQueue(string? connection) => connection != null && native.TryGetValue(connection,out bool queue) && queue;
    public static void Remove(string connection) => native.TryRemove(connection, out _);
}
