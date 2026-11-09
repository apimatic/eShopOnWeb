using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>In-process mutual exclusion per key (an order, a shopper email).</summary>
public static class KeyedLocks
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    public static async Task<IDisposable> Acquire(string key)
    {
        var gate = Locks.GetOrAdd(key.ToLowerInvariant(), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;

        public Releaser(SemaphoreSlim gate) => _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
