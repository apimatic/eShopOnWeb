using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionOperationLock : ISubscriptionOperationLock
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async ValueTask<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
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
            RemoveReference(key, entry, false);
            throw;
        }
    }

    private void RemoveReference(string key, Entry entry, bool release)
    {
        if (release)
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

    private sealed class Releaser : IAsyncDisposable
    {
        private readonly SubscriptionOperationLock _owner;
        private readonly string _key;
        private Entry? _entry;

        public Releaser(SubscriptionOperationLock owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public ValueTask DisposeAsync()
        {
            var entry = Interlocked.Exchange(ref _entry, null);
            if (entry != null)
            {
                _owner.RemoveReference(_key, entry, true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
