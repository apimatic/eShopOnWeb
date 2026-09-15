using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Process-wide per-key mutual exclusion so concurrent payment operations on the same order are
/// serialised — a double-click cannot authorize or capture twice. Registered as a singleton.
/// (For a multi-host deployment this would be backed by a distributed lock; with the single-host
/// in-memory store this app runs against, an in-process lock is sufficient and correct.)
/// </summary>
public class KeyedPaymentLock : IPaymentLock
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
            if (_released)
            {
                return;
            }
            _released = true;
            _gate.Release();
        }
    }
}
