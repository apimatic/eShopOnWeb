using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class OrderOperationLock
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        private int _released;
        public Releaser(SemaphoreSlim gate) => _gate = gate;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) _gate.Release();
        }
    }
}
