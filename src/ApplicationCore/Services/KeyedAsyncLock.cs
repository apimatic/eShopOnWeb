using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Serializes work per key within the process. Payment operations on a single order
/// (authorize/capture/void/refund) run under the order's lock so a double-click can never
/// run two mutating operations concurrently and double-charge the shopper. Combined with
/// the local status checks and PayPal's own PayPal-Request-Id idempotency, this makes the
/// operations idempotent in effect.
/// </summary>
public sealed class KeyedAsyncLock
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    public async Task<IDisposable> LockAsync(string key, CancellationToken ct = default)
    {
        var gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
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
