using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal interface ISubscriptionOperationLock
{
    ValueTask<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken);
}

internal sealed class SubscriptionOperationLock : ISubscriptionOperationLock
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async ValueTask<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        Entry entry;
        while (true)
        {
            entry = _entries.GetOrAdd(key, static _ => new Entry());
            Interlocked.Increment(ref entry.Users);
            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(entry, current))
            {
                break;
            }

            Interlocked.Decrement(ref entry.Users);
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new Releaser(this, key, entry);
        }
        catch
        {
            ReleaseReference(key, entry, releaseSemaphore: false);
            throw;
        }
    }

    private void ReleaseReference(string key, Entry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            entry.Semaphore.Release();
        }

        if (Interlocked.Decrement(ref entry.Users) == 0)
        {
            _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
        }
    }

    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Users;
    }

    private sealed class Releaser(
        SubscriptionOperationLock owner,
        string key,
        Entry entry) : IAsyncDisposable
    {
        private SubscriptionOperationLock? _owner = owner;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _owner, null)?.ReleaseReference(key, entry, releaseSemaphore: true);
            return ValueTask.CompletedTask;
        }
    }
}
