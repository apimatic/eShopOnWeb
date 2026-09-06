using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// An in-process keyed mutex. Registered as a singleton, it guarantees that only one request per
/// shopper is inside the subscribe flow at a time on this instance.
/// </summary>
/// <remarks>
/// Deliberately scoped to one process: it is the cheap first line of defence against a double
/// click, not a distributed lock. Correctness across instances comes from re-reading the
/// shopper's enrollments and from the uniqueness token sent to the billing system.
/// </remarks>
public class SubscriberLock : ISubscriberLock
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var entry = Rent(key);

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            Return(key);
            throw;
        }

        return new Release(this, key, entry);
    }

    // Reference counting keeps the dictionary from growing one entry per shopper forever.
    private Entry Rent(string key)
    {
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                entry = new Entry();
                _entries[key] = entry;
            }

            entry.Waiters++;
            return entry;
        }
    }

    private void Return(string key)
    {
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out var entry)) return;

            if (--entry.Waiters <= 0)
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

    private sealed class Release : IDisposable
    {
        private readonly SubscriberLock _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private int _disposed;

        public Release(SubscriberLock owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            _entry.Semaphore.Release();
            _owner.Return(_key);
        }
    }
}
