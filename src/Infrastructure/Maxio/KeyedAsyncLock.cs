using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Per-key async mutual exclusion. Serializes the check-then-create subscribe flow for a given
/// user so concurrent double-clicks cannot each create a customer/subscription. This guarantees
/// idempotency within a single host; Maxio's own uniqueness_token is TOCTOU-prone under true
/// concurrency and cannot be relied upon for this. (A multi-instance deployment would additionally
/// need a distributed lock; the per-attempt uniqueness token narrows, but does not close, that gap.)
/// </summary>
public sealed class KeyedAsyncLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
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
            if (_released)
                return;
            _released = true;
            _gate.Release();
        }
    }
}
