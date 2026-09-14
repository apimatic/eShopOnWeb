using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Serializes subscribe requests for one shopper, so two requests that arrive together - the
/// double-clicked button - cannot both pass the "is this shopper already subscribed?" check before
/// either of them writes.
/// </summary>
/// <remarks>
/// The billing provider offers no idempotency key on subscription creation and documents no
/// uniqueness on a subscription reference, so read-then-write is the only provider-side tool and it
/// is inherently racy. This registry closes that race within a process. It is not a distributed
/// lock: behind more than one instance the read-then-write check still narrows the window, and the
/// provider-enforced uniqueness of the *customer* reference still guarantees one billing customer
/// per shopper, but a genuinely simultaneous double subscribe across instances would need a shared
/// lock or a unique constraint in a shared store.
/// </remarks>
public sealed class SubscriberLockRegistry
{
    private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
    private readonly object _sync = new object();

    /// <summary>Acquires the lock for <paramref name="key"/>; dispose the result to release it.</summary>
    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries.Add(key, entry);
            }

            entry.RefCount++;
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

        lock (_sync)
        {
            entry.RefCount--;
            if (entry.RefCount == 0)
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1, 1);
        public int RefCount;
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SubscriberLockRegistry _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private int _disposed;

        public Releaser(SubscriberLockRegistry owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Release(_key, _entry, entered: true);
            }
        }
    }
}
