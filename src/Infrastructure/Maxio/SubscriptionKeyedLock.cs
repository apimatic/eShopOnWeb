using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal sealed class SubscriptionKeyedLock
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        Entry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries.Add(key, entry);
            }

            entry.ReferenceCount++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new Releaser(this, key, entry);
        }
        catch
        {
            RemoveReference(key, entry, releaseSemaphore: false);
            throw;
        }
    }

    private void RemoveReference(string key, Entry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            entry.Semaphore.Release();
        }

        lock (_gate)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0)
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int ReferenceCount { get; set; }
    }

    private sealed class Releaser : IDisposable
    {
        private SubscriptionKeyedLock? _owner;
        private readonly string _key;
        private readonly Entry _entry;

        public Releaser(SubscriptionKeyedLock owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.RemoveReference(_key, _entry, releaseSemaphore: true);
        }
    }
}
