using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serialises work per key inside this process, so two concurrent requests for the same key run
/// one after the other rather than racing.
/// </summary>
/// <remarks>
/// This is what turns "check, then create" into a safe sequence for a double-clicked subscribe
/// button: both requests carry the same customer reference, so the second waits and then observes
/// the subscription the first one created.
/// <para>
/// The guarantee is per process. Across a scaled-out deployment the remaining protection is
/// Maxio's own uniqueness constraint on the customer reference (a duplicate create comes back 422
/// and is resolved by re-reading the customer), plus the existing-subscription check performed
/// immediately before creating. A cross-instance lock would need shared state, which this
/// integration deliberately does not introduce.
/// </para>
/// </remarks>
public sealed class KeyedAsyncLock : IDisposable
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>
    /// Waits for exclusive access to <paramref name="key"/>. Dispose the returned handle to release it.
    /// </summary>
    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        Entry entry;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries[key] = entry;
            }

            // Counted while holding the gate so the entry cannot be removed between here and the wait.
            entry.Waiters++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            Release(key, entry, acquired: false);
            throw;
        }

        return new Handle(this, key, entry);
    }

    private void Release(string key, Entry entry, bool acquired)
    {
        if (acquired)
        {
            entry.Semaphore.Release();
        }

        lock (_gate)
        {
            entry.Waiters--;
            if (entry.Waiters == 0 && _entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
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

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        /// <summary>Requests currently holding or waiting for this key; the entry lives while it is non-zero.</summary>
        public int Waiters { get; set; }
    }

    private sealed class Handle : IDisposable
    {
        private readonly KeyedAsyncLock _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private int _released;

        public Handle(KeyedAsyncLock owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _owner.Release(_key, _entry, acquired: true);
            }
        }
    }
}
