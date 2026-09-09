using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Serialises work per key so concurrent requests for the same order (e.g. a double-clicked
/// "pay" or "fulfil") cannot race past each other's state checks. Registered as a singleton.
/// </summary>
public sealed class KeyedAsyncLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

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
