using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

public sealed class SubscriptionIntentCoordinator
{
    private readonly ConcurrentDictionary<string, LockEntry> _locks = new(StringComparer.Ordinal);

    public async ValueTask<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        while (true)
        {
            var entry = _locks.GetOrAdd(key, static _ => new LockEntry());
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new Releaser(entry);
        }
    }

    private sealed class LockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private LockEntry? _entry;

        public Releaser(LockEntry entry) => _entry = entry;

        public ValueTask DisposeAsync()
        {
            var entry = Interlocked.Exchange(ref _entry, null);
            if (entry is not null)
            {
                entry.Semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
