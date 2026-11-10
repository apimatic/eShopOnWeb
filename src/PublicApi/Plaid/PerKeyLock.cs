using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>
/// Serializes work per key (a checkout, a connection) inside this process, so that two simultaneous requests for
/// the same thing can't both charge the shopper or both advance the same sync cursor.
/// </summary>
public class PerKeyLock
{
    private readonly Dictionary<string, (SemaphoreSlim Gate, int Users)> _gates = new();

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        SemaphoreSlim gate;
        lock (_gates)
        {
            if (!_gates.TryGetValue(key, out var entry))
                entry = (new SemaphoreSlim(1, 1), 0);
            _gates[key] = (entry.Gate, entry.Users + 1);
            gate = entry.Gate;
        }

        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Release(key, gate, false);
            throw;
        }

        return new Releaser(() => Release(key, gate, true));
    }

    private void Release(string key, SemaphoreSlim gate, bool held)
    {
        if (held)
            gate.Release();
        lock (_gates)
        {
            var entry = _gates[key];
            if (entry.Users <= 1)
                _gates.Remove(key);
            else
                _gates[key] = (entry.Gate, entry.Users - 1);
        }
    }

    private sealed class Releaser : IDisposable
    {
        private Action? _release;
        public Releaser(Action release) => _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
