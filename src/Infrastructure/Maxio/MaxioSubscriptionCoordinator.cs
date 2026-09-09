using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serializes enrollment work per customer reference within this process. Two concurrent
/// subscribe calls for the same user (e.g. a double-click) are forced to run one after the
/// other, so the "look up then create" customer/subscription steps cannot race into creating
/// duplicates. Registered as a singleton.
/// </summary>
public sealed class MaxioSubscriptionCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> LockAsync(string reference, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
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
