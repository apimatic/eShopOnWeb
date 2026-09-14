using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Serialises enrollment work per shopper inside this process, so a double-clicked subscribe
/// cannot run its "does a subscription already exist?" check before the first click has finished
/// creating one. Across processes the duplicate-prevention token sent with every create is the
/// backstop; this lock removes the common single-instance race outright.
/// Registered as a singleton.
/// </summary>
public sealed class SubscriberKeyedLock
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
                _entries.Add(key, entry);
            }

            entry.Waiters++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            ReleaseWaiter(key, entry);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void ReleaseWaiter(string key, Entry entry)
    {
        lock (_entries)
        {
            if (--entry.Waiters == 0 && _entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            {
                _entries.Remove(key);
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        /// <summary>Holders plus waiters; guarded by the owning dictionary's monitor.</summary>
        public int Waiters { get; set; }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SubscriberKeyedLock _owner;
        private readonly string _key;
        private Entry? _entry;

        public Releaser(SubscriberKeyedLock owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            var entry = Interlocked.Exchange(ref _entry, null);
            if (entry is null)
            {
                return;
            }

            entry.Semaphore.Release();
            _owner.ReleaseWaiter(_key, entry);
        }
    }
}
