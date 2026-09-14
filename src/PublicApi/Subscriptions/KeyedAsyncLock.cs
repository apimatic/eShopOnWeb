using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Provides an async mutex per key (e.g. per user) so operations that must be serialized per
/// actor (subscribe) cannot interleave. The scope of the lock is this process only; it exists to
/// make concurrent double-clicks idempotent.
/// </summary>
public sealed class KeyedAsyncLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IAsyncDisposable> LockAsync(string key, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(key, semaphore, _locks);
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private readonly string _key;
        private readonly SemaphoreSlim _semaphore;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks;

        public Releaser(string key, SemaphoreSlim semaphore, ConcurrentDictionary<string, SemaphoreSlim> locks)
        {
            _key = key;
            _semaphore = semaphore;
            _locks = locks;
        }

        public ValueTask DisposeAsync()
        {
            _semaphore.Release();
            // Best-effort cleanup when nobody is waiting; safe to leave behind otherwise.
            if (_semaphore.CurrentCount == 1)
            {
                _locks.TryRemove(_key, out _);
            }

            return ValueTask.CompletedTask;
        }
    }
}
