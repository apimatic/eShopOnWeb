namespace Microsoft.eShopWeb.MaxioBilling.Internal;

/// <summary>
/// Serializes subscribe requests per customer reference within this process.
/// <para>
/// Maxio enforces uniqueness on a customer reference but documents none for a subscription, and
/// <c>CreateSubscription</c> takes no idempotency key — so the "does a live subscription already
/// exist?" check and the create that follows it are not atomic at the provider. Two simultaneous
/// requests that both read "no subscription" would both create one. Holding a per-subscriber lock
/// across the check and the create closes that window.
/// </para>
/// <para>
/// This is a single-process guard. Behind more than one instance of the API it must be replaced by
/// a distributed lock (or a uniqueness constraint in the application's own store); the check-then-
/// create and the reconcile-after-failure below still apply either way.
/// </para>
/// </summary>
internal sealed class SubscriberLocks
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries[key] = entry;
            }

            entry.Waiters++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            Release(key, entry, entered: false);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void Release(string key, Entry entry, bool entered)
    {
        if (entered)
        {
            entry.Semaphore.Release();
        }

        lock (_entries)
        {
            entry.Waiters--;
            if (entry.Waiters == 0 && _entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Waiters { get; set; }
    }

    private sealed class Releaser(SubscriberLocks owner, string key, Entry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Release(key, entry, entered: true);
            }
        }
    }
}
