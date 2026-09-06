using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serializes enrollment for one subscriber so the read-then-create sequence cannot interleave with
/// itself. Two concurrent POSTs from a double-click would otherwise both observe "no subscription yet"
/// and both create one; the billing provider offers no idempotency key to prevent that server-side.
/// </summary>
/// <remarks>
/// This is a per-process gate. It closes the double-click window that this application is responsible
/// for; behind a load balancer with several instances, the read-before-create check plus the
/// deterministic subscription reference remain the cross-instance guard.
/// </remarks>
public sealed class SubscriberEnrollmentLock : IDisposable
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>Acquires the gate for <paramref name="key"/>; dispose the result to release it.</summary>
    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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
                // A concurrent Return may have evicted this entry between GetOrAdd and the lock; retry.
                if (entry.Evicted)
                {
                    continue;
                }

                entry.Waiters++;
                return entry;
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

            entry.Evicted = true;
        }

        _entries.TryRemove(key, out _);
        entry.Semaphore.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var key in _entries.Keys)
        {
            if (_entries.TryRemove(key, out var entry))
            {
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Waiters { get; set; }
        public bool Evicted { get; set; }
    }

    private sealed class Release : IDisposable
    {
        private readonly SubscriberEnrollmentLock _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private bool _released;

        public Release(SubscriberEnrollmentLock owner, string key, Entry entry)
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
