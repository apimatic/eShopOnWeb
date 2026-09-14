using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serialises work per key within this process.
/// </summary>
/// <remarks>
/// Used to funnel a single shopper's concurrent subscribe requests - the double-clicked button -
/// through the find-or-create path one at a time. It is deliberately in-process only: correctness
/// across instances comes from Maxio's own uniqueness constraints on customer and subscription
/// references, which this lock merely avoids hitting in the common case.
/// </remarks>
public sealed class KeyedAsyncLock
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries.Add(key, entry);
            }

            entry.Waiters++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            Release(key, entry, semaphoreHeld: false);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void Release(string key, Entry entry, bool semaphoreHeld)
    {
        if (semaphoreHeld)
        {
            entry.Semaphore.Release();
        }

        lock (_entries)
        {
            entry.Waiters--;
            if (entry.Waiters == 0 && _entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
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
            _owner.Release(_key, _entry, semaphoreHeld: true);
        }
    }
}
