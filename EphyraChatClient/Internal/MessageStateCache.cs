using System.Collections.Concurrent;

namespace EphyraChatClient.Internal;

/// <summary>
/// Remembers the state sequence of messages as they go past. Edits and deletes have to quote the current one back to
/// the host (optimistic locking) and nobody calling EditMessageAsync wants to keep track of that themselves.
/// </summary>
internal class MessageStateCache(int capacity = 100_000)
{
    private readonly ConcurrentDictionary<string, long> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _order = new();

    public void Update(string? messageUuid, long? stateSeq)
    {
        if (messageUuid == null || stateSeq == null) return;
        var added = false;
        _states.AddOrUpdate(messageUuid, _ =>
        {
            added = true;
            return stateSeq.Value;
        }, (_, existing) => Math.Max(existing, stateSeq.Value));
        if (!added) return;
        _order.Enqueue(messageUuid);
        while (_states.Count > capacity && _order.TryDequeue(out var oldest))
        {
            _states.TryRemove(oldest, out _);
        }
    }

    public long? Get(string messageUuid)
    {
        return _states.TryGetValue(messageUuid, out var value) ? value : null;
    }

    public void Remove(string messageUuid)
    {
        _states.TryRemove(messageUuid, out _);
    }
}
