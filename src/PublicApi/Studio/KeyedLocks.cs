using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// A fixed set of striped semaphores: serialises work for the same key without the memory
/// growth of a per-key dictionary in a process that runs for weeks.
/// </summary>
public sealed class KeyedLocks
{
    private readonly SemaphoreSlim[] _locks;

    public KeyedLocks(int stripes = 256)
    {
        _locks = new SemaphoreSlim[stripes];
        for (var i = 0; i < stripes; i++) _locks[i] = new SemaphoreSlim(1, 1);
    }

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken ct)
    {
        var index = (int)((uint)StringComparer.OrdinalIgnoreCase.GetHashCode(key) % (uint)_locks.Length);
        var sem = _locks[index];
        await sem.WaitAsync(ct).ConfigureAwait(false);
        return new Releaser(sem);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _sem;
        public Releaser(SemaphoreSlim sem) => _sem = sem;
        public void Dispose() => Interlocked.Exchange(ref _sem, null)?.Release();
    }
}
