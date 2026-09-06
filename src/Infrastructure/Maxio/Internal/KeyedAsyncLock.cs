using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Internal;

/// <summary>
/// Serialises work per key within a single process.
/// <para>
/// This is the first line of defence against a double-clicked subscribe: two requests for the same
/// shopper queue behind each other, so the second observes the subscription the first created
/// instead of racing it. Cross-process races remain covered by the reference uniqueness and the
/// uniqueness token Maxio itself enforces — this removes the common case cheaply.
/// </para>
/// Entries are reference-counted and dropped once uncontended, so the map only ever holds keys with
/// live contention rather than growing with every shopper the process has ever served.
/// </summary>
public sealed class KeyedAsyncLock
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var existing))
            {
                existing = new Entry();
                _entries[key] = existing;
            }

            existing.RefCount++;
            entry = existing;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            DropRef(key, entry);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void Release(string key, Entry entry)
    {
        entry.Semaphore.Release();
        DropRef(key, entry);
    }

    private void DropRef(string key, Entry entry)
    {
        lock (_gate)
        {
            if (--entry.RefCount > 0)
            {
                return;
            }

            _entries.Remove(key);
        }

        // Safe to dispose: the ref count reached zero under the gate, so no acquirer holds or is
        // about to wait on this semaphore.
        entry.Semaphore.Dispose();
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int RefCount { get; set; }
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
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Release(_key, _entry);
            }
        }
    }
}
