using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;

/// <summary>
/// Serialises subscribe attempts for a single shopper inside this process, so the common double-click
/// never turns into two concurrent round-trips racing each other's "does a subscription already exist?"
/// check. It is a latency optimisation, not the correctness guarantee: correctness comes from the
/// uniqueness Maxio enforces on the customer and subscription <c>reference</c>, which also covers the
/// multi-instance case this in-process lock cannot see.
///
/// Entries are reference counted and dropped once nobody holds or wants them, so a long-running process
/// does not accumulate a semaphore per shopper that ever subscribed.
/// </summary>
public sealed class SubscriberLockProvider : IDisposable
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private bool _disposed;

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var entry = Rent(key);

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Return(key, entry, released: false);
            throw;
        }

        return new Release(this, key, entry);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var entry in _entries.Values)
            {
                entry.Semaphore.Dispose();
            }

            _entries.Clear();
        }
    }

    /// <summary>Keys currently holding a semaphore. Exposed so tests can assert entries are not leaked.</summary>
    internal int TrackedKeyCount
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    private Entry Rent(string key)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_entries.TryGetValue(key, out var entry))
            {
                entry = new Entry();
                _entries[key] = entry;
            }

            entry.Users++;
            return entry;
        }
    }

    private void Return(string key, Entry entry, bool released)
    {
        lock (_gate)
        {
            if (released)
            {
                entry.Semaphore.Release();
            }

            if (--entry.Users > 0 || _disposed)
            {
                return;
            }

            // Nobody holds the lock or is waiting for it, so the entry can go. A later caller for the
            // same shopper simply creates a fresh one.
            _entries.Remove(key);
            entry.Semaphore.Dispose();
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        /// <summary>Callers currently holding or waiting for this lock. Guarded by the provider's gate.</summary>
        public int Users { get; set; }
    }

    private sealed class Release : IDisposable
    {
        private readonly SubscriberLockProvider _owner;
        private readonly string _key;
        private Entry? _entry;

        public Release(SubscriberLockProvider owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            var entry = Interlocked.Exchange(ref _entry, null);

            if (entry is not null)
            {
                _owner.Return(_key, entry, released: true);
            }
        }
    }
}
