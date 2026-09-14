using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A process-wide, per-key async mutex. Subscribe requests for the same shopper are serialized
/// so that a genuine double-click (two concurrent requests) can't race past the find-or-create
/// checks and produce a duplicate customer or subscription. Registered as a singleton so the key
/// map is shared across all scoped billing services in the process.
/// </summary>
/// <remarks>
/// This guards concurrency within a single running process only. Across processes (or process
/// restarts) idempotency still holds, because it is ultimately enforced against Maxio itself —
/// the unique customer <c>reference</c> and the existing-active-subscription check.
/// </remarks>
public sealed class KeyedAsyncLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken = default)
    {
        var gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;
        public Releaser(SemaphoreSlim gate) => _gate = gate;

        public void Dispose()
        {
            var gate = Interlocked.Exchange(ref _gate, null);
            gate?.Release();
        }
    }
}
