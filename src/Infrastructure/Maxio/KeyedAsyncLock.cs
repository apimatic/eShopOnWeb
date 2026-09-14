using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serialises work per key within the process.
/// </summary>
/// <remarks>
/// This is the first line of defence against a double-clicked subscribe button: two requests for the
/// same shopper run one after the other, so the second one sees the subscription the first one created
/// instead of racing it. It is deliberately only a local guard - the durable guarantees come from the
/// unique references and uniqueness tokens sent to the billing system, which also hold across
/// instances and restarts.
/// </remarks>
public sealed class KeyedAsyncLock
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var entry = Rent(key);

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            Return(key, entry);
            throw;
        }

        return new Release(this, key, entry);
    }

    private Entry Rent(string key)
    {
        while (true)
        {
            var entry = _entries.GetOrAdd(key, _ => new Entry());

            lock (entry)
            {
                // A holder that has already been retired from the dictionary must not be joined, or two
                // callers could end up waiting on different semaphores for the same key.
                if (!entry.Retired)
                {
                    entry.Waiters++;
                    return entry;
                }
            }

            // The retiring caller removes the entry from the dictionary moments after marking it, so
            // yield rather than spinning hot while that happens.
            Thread.Yield();
        }
    }

    private void Return(string key, Entry entry)
    {
        lock (entry)
        {
            entry.Waiters--;
            if (entry.Waiters > 0)
            {
                return;
            }

            entry.Retired = true;
        }

        _entries.TryRemove(new System.Collections.Generic.KeyValuePair<string, Entry>(key, entry));
        entry.Semaphore.Dispose();
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Waiters { get; set; }
        public bool Retired { get; set; }
    }

    private sealed class Release : IDisposable
    {
        private readonly KeyedAsyncLock _owner;
        private readonly string _key;
        private Entry? _entry;

        public Release(KeyedAsyncLock owner, string key, Entry entry)
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
            _owner.Return(_key, entry);
        }
    }
}
