using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// In-process keyed lock. Registered as a singleton. Each distinct key gets its own
/// <see cref="SemaphoreSlim"/>; acquiring returns a disposable that releases on dispose.
/// </summary>
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
        private bool _disposed;

        public Releaser(SemaphoreSlim gate) => _gate = gate;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _gate.Release();
        }
    }
}
