using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Serialises concurrent work that shares a key, without holding a semaphore per key
/// forever: each gate is reference counted and dropped once nobody is using it.
/// </summary>
/// <remarks>
/// This is an in-process guard only. It closes the common double-click window cheaply,
/// but correctness never depends on it - the callers that use it also enforce their
/// invariant against the billing system itself.
/// </remarks>
public sealed class KeyedAsyncLock
{
    private sealed class Gate
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int WaiterCount;
    }

    private readonly Dictionary<string, Gate> _gates = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    /// <summary>Waits for exclusive access to <paramref name="key"/>; dispose to release.</summary>
    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));

        Gate gate;
        lock (_sync)
        {
            if (!_gates.TryGetValue(key, out gate!))
            {
                gate = new Gate();
                _gates.Add(key, gate);
            }

            gate.WaiterCount++;
        }

        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Release(key, gate, releaseSemaphore: false);
            throw;
        }

        return new Releaser(this, key, gate);
    }

    private void Release(string key, Gate gate, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            gate.Semaphore.Release();
        }

        lock (_sync)
        {
            if (--gate.WaiterCount == 0)
            {
                _gates.Remove(key);
                gate.Semaphore.Dispose();
            }
        }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly KeyedAsyncLock _owner;
        private readonly string _key;
        private readonly Gate _gate;
        private int _disposed;

        public Releaser(KeyedAsyncLock owner, string key, Gate gate)
        {
            _owner = owner;
            _key = key;
            _gate = gate;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Release(_key, _gate, releaseSemaphore: true);
            }
        }
    }
}
