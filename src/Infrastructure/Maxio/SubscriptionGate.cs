using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Process-wide keyed mutex used to serialize subscribe attempts for the same customer + plan.
/// Maxio offers no create-or-find for subscriptions and does not document enforcing uniqueness of
/// a client-supplied subscription reference, so under true concurrency (a double-click) two
/// requests could both pass the "no active subscription yet" pre-check and create two
/// subscriptions. Serializing per (customer, plan) makes the second request run after the first
/// created its subscription, so the pre-check then returns the existing one. This guarantees
/// single-subscription semantics within one host.
/// </summary>
internal sealed class SubscriptionGate
{
    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new SemaphoreSlim(1, 1);
        public int RefCount;
    }

    // The bookkeeping lock serializes acquire/release/removal so that entry removal can never race
    // a new acquire for the same key (which would otherwise hand a waiter a disposed semaphore).
    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> EnterAsync(string key, CancellationToken cancellationToken)
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
            // Never acquired: drop our reference without releasing the semaphore.
            Deregister(key, entry);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void Release(string key, Entry entry)
    {
        // Release first so a queued waiter can proceed; it is still counted in RefCount, so the
        // entry is only removed once the last holder/waiter has finished with it.
        entry.Semaphore.Release();
        Deregister(key, entry);
    }

    private void Deregister(string key, Entry entry)
    {
        var dispose = false;
        lock (_sync)
        {
            entry.RefCount--;
            if (entry.RefCount <= 0
                && _entries.TryGetValue(key, out var current)
                && ReferenceEquals(current, entry))
            {
                _entries.Remove(key);
                dispose = true;
            }
        }

        if (dispose)
        {
            entry.Semaphore.Dispose();
        }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SubscriptionGate _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private int _disposed;

        public Releaser(SubscriptionGate owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Release(_key, _entry);
            }
        }
    }
}
