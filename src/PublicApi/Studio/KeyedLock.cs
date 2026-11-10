using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// A fixed set of locks picked by key hash: requests for the same key never run at the same time, and
/// the memory used never grows, however many distinct keys come by over weeks of uptime.
/// </summary>
public sealed class KeyedLock
{
    private readonly SemaphoreSlim[] _locks;

    public KeyedLock(int stripes = 256)
    {
        _locks = new SemaphoreSlim[stripes];
        for (var i = 0; i < stripes; i++)
        {
            _locks[i] = new SemaphoreSlim(1, 1);
        }
    }

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken ct)
    {
        var index = (int)((uint)StringComparer.Ordinal.GetHashCode(key) % (uint)_locks.Length);
        var gate = _locks[index];
        await gate.WaitAsync(ct).ConfigureAwait(false);
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;
        public Releaser(SemaphoreSlim gate) => _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
