using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Serializes work per key within this process, so two concurrent subscribe requests for the same shopper
/// (the double-click case) run one after the other and the second sees the first one's result.
/// <para>
/// This is an in-process guard. Across multiple application instances the reference-keyed find-or-create
/// and the pre-existing-subscription check remain the durable defences; Maxio exposes no idempotency key
/// on subscription creation.
/// </para>
/// </summary>
internal sealed class KeyedAsyncLock
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

        return new Releaser(this, key, entry);
    }

    private Entry Rent(string key)
    {
        while (true)
        {
            var entry = _entries.GetOrAdd(key, _ => new Entry());

            lock (entry.Gate)
            {
                // Lost a race with a Return() that already evicted this entry — retry with a fresh one.
                if (!entry.Evicted)
                {
                    entry.Waiters++;
                    return entry;
                }
            }
        }
    }

    private void Return(string key, Entry entry)
    {
        lock (entry.Gate)
        {
            entry.Waiters--;
            if (entry.Waiters > 0)
            {
                return;
            }

            entry.Evicted = true;
        }

        _entries.TryRemove(new System.Collections.Generic.KeyValuePair<string, Entry>(key, entry));
        entry.Semaphore.Dispose();
    }

    private sealed class Entry
    {
        public readonly object Gate = new();
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Waiters;
        public bool Evicted;
    }

    private sealed class Releaser : IDisposable
    {
        private readonly KeyedAsyncLock _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private bool _released;

        public Releaser(KeyedAsyncLock owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _entry.Semaphore.Release();
            _owner.Return(_key, _entry);
        }
    }
}
