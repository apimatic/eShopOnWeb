using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serialises work per key inside one process. Used so that two simultaneous subscribe calls for the
/// same shopper cannot both decide that no customer/subscription exists yet.
/// </summary>
/// <remarks>
/// This closes the double-click window for a single instance; correctness across instances still
/// relies on the lookup-before-create pass against the billing provider, which is the system of record.
/// </remarks>
public sealed class KeyedAsyncLock
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _syncRoot = new();

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var entry = Rent(key);

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Return(key);
            throw;
        }

        return new Releaser(this, key, entry.Semaphore);
    }

    private Entry Rent(string key)
    {
        lock (_syncRoot)
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
        lock (_syncRoot)
        {
            if (_entries.TryGetValue(key, out var entry) && --entry.Waiters <= 0)
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

    private sealed class Releaser : IDisposable
    {
        private readonly KeyedAsyncLock _owner;
        private readonly string _key;
        private readonly SemaphoreSlim _semaphore;
        private bool _released;

        public Releaser(KeyedAsyncLock owner, string key, SemaphoreSlim semaphore)
        {
            _owner = owner;
            _key = key;
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _semaphore.Release();
            _owner.Return(_key);
        }
    }
}
