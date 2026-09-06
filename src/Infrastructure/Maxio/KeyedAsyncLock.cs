using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serialises work per key within this process.
/// </summary>
/// <remarks>
/// Maxio exposes no idempotency key on subscription creation, so "check whether the shopper is
/// already subscribed, then create" cannot be made atomic at the provider. This narrows the window
/// to nothing for the case that actually happens — the same shopper double-clicking, or a client
/// retrying — by letting only one subscribe run per shopper at a time. It is a single-process
/// guard: a multi-instance deployment would need a shared lease (a row lock, a distributed lock)
/// behind the same interface.
/// </remarks>
public sealed class KeyedAsyncLock
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var entry = Rent(key);
        try
        {
            if (!await entry.Semaphore.WaitAsync(timeout, cancellationToken))
            {
                Return(key, entry);
                return null!;
            }
        }
        catch
        {
            Return(key, entry);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private Entry Rent(string key)
    {
        while (true)
        {
            var entry = _entries.GetOrAdd(key, _ => new Entry());
            lock (entry)
            {
                // A concurrent Release may already have retired this entry; if so, retry the lookup.
                if (!entry.Retired)
                {
                    entry.Waiters++;
                    return entry;
                }
            }
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
    }

    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Waiters;
        public bool Retired;
    }

    private sealed class Releaser : IDisposable
    {
        private readonly KeyedAsyncLock _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private int _disposed;

        public Releaser(KeyedAsyncLock owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _entry.Semaphore.Release();
            _owner.Return(_key, _entry);
        }
    }
}
