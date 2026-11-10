using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Serialises work for the same key (e.g. the same shopper) without growing for the life of the process:
/// a fixed set of striped semaphores is shared by all keys.
/// </summary>
public sealed class KeyedLock
{
    private readonly SemaphoreSlim[] _stripes;

    public KeyedLock(int stripes = 128)
    {
        _stripes = new SemaphoreSlim[stripes];
        for (var i = 0; i < stripes; i++) _stripes[i] = new SemaphoreSlim(1, 1);
    }

    public async Task<IDisposable> AcquireAsync(string key)
    {
        var stripe = _stripes[(uint)StringComparer.OrdinalIgnoreCase.GetHashCode(key) % (uint)_stripes.Length];
        await stripe.WaitAsync().ConfigureAwait(false);
        return new Releaser(stripe);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;
        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
