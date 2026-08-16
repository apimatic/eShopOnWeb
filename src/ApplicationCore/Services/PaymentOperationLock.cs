using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Serializes payment operations per order so a genuine concurrent double-click can't slip two
/// authorizations or captures past the "already done?" state check. Registered as a singleton; the
/// in-memory, single-host deployment this runs on makes a process-local lock sufficient.
/// </summary>
public interface IPaymentOperationLock
{
    Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default);
}

public class PaymentOperationLock : IPaymentOperationLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        private bool _released;

        public Releaser(SemaphoreSlim gate) => _gate = gate;

        public void Dispose()
        {
            if (_released) return;
            _released = true;
            _gate.Release();
        }
    }
}
