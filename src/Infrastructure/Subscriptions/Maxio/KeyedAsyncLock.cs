using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;

/// <summary>
/// Serializes concurrent operations that share the same key, within this process. Used to make the
/// subscribe flow safe against a rapid double-click for the same subscriber, so two near-simultaneous
/// requests cannot both pass the "no existing subscription" check and each create one.
/// <para>
/// This is best-effort, single-process coordination (the app runs with an in-memory store and no external
/// coordinator); combined with Maxio-side idempotency on the customer reference it prevents duplicate
/// customers and, in practice, duplicate subscriptions from a double-click.
/// </para>
/// </summary>
public sealed class KeyedAsyncLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _released;

        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose()
        {
            if (_released) return;
            _released = true;
            _semaphore.Release();
        }
    }
}
