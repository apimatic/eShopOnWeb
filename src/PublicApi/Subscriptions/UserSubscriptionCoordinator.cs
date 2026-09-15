using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IUserSubscriptionCoordinator
{
    Task<IDisposable> EnterAsync(string userReference, CancellationToken cancellationToken);
}

public sealed class UserSubscriptionCoordinator : IUserSubscriptionCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> EnterAsync(string userReference, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(userReference, static _ => new SemaphoreSlim(1, 1));
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
