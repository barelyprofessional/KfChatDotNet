namespace EphyraChatClient.Internal;

/// <summary>
/// One async lock per key that cleans up after itself. Used so operations against the same message go out one at a
/// time, as each one needs the state sequence the one before it produced.
/// </summary>
internal class KeyedLock
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int References;
    }

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken ct)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries[key] = entry;
            }
            entry.References++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(ct);
        }
        catch
        {
            Dereference(key, entry);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void Dereference(string key, Entry entry)
    {
        lock (_entries)
        {
            entry.References--;
            if (entry.References == 0) _entries.Remove(key);
        }
    }

    private class Releaser(KeyedLock owner, string key, Entry entry) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 1) return;
            entry.Semaphore.Release();
            owner.Dereference(key, entry);
        }
    }
}
