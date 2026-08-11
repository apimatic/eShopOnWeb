using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Serialises payment operations per order within a process so a double-clicked request cannot
/// authorize or capture the shopper twice. PayPal's per-request idempotency key is the second line
/// of defence; this lock removes the race between two in-flight requests for the same order.
/// Registered as a singleton so the lock is shared across scoped service instances.
/// </summary>
public interface IOrderOperationLock
{
    Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class OrderOperationLock : IOrderOperationLock
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
