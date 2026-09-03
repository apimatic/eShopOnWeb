using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionOperationCoordinator
{
    private readonly ConcurrentDictionary<string, LockEntry> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        LockEntry entry;
        while (true)
        {
            entry = _locks.GetOrAdd(key, static _ => new LockEntry());
            lock (entry.Gate)
            {
                if (entry.Retired)
                {
                    continue;
                }

                entry.Leases++;
                break;
            }
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new Releaser(key, entry, _locks);
        }
        catch
        {
            ReleaseLease(key, entry, _locks);
            throw;
        }
    }

    private static void ReleaseLease(
        string key,
        LockEntry entry,
        ConcurrentDictionary<string, LockEntry> locks)
    {
        lock (entry.Gate)
        {
            entry.Leases--;
            if (entry.Leases == 0)
            {
                entry.Retired = true;
                locks.TryRemove(new KeyValuePair<string, LockEntry>(key, entry));
            }
        }
    }

    private sealed class LockEntry
    {
        public object Gate { get; } = new();
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Leases { get; set; }
        public bool Retired { get; set; }
    }

    private sealed class Releaser(
        string key,
        LockEntry entry,
        ConcurrentDictionary<string, LockEntry> locks) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            entry.Semaphore.Release();
            ReleaseLease(key, entry, locks);
        }
    }
}
