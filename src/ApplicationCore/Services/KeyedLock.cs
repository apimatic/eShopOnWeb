using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// A process-wide, per-key async mutex. Payment operations on the same order are serialized through this
/// so a double-click can never authorize or capture twice within a host. Registered as a singleton.
/// </summary>
public sealed class KeyedLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> LockAsync(string key, CancellationToken ct = default)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;

        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose()
        {
            var semaphore = Interlocked.Exchange(ref _semaphore, null);
            semaphore?.Release();
        }
    }
}
