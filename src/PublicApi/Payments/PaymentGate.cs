using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Serializes the money-moving operations for one order inside this host. The durable claims in
/// the database remain the cross-process guard; this gate makes the in-process double-click
/// (and the in-memory test database) deterministic.
/// </summary>
public sealed class PaymentGate
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _gates = new();

    public async Task<IDisposable> LockAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = _gates.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
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
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _gate.Release();
            }
        }
    }
}
