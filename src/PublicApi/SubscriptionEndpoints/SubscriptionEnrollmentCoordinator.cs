using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionEnrollmentCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string userId, string productHandle, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd($"{userId}:{productHandle}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        public Releaser(SemaphoreSlim gate) => _gate = gate;
        public void Dispose() => _gate.Release();
    }
}
