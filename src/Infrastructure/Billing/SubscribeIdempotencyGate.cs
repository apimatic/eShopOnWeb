using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Per-process gate so a double-click on Subscribe cannot race two Maxio signups.
/// </summary>
public sealed class SubscribeIdempotencyGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public async Task<T> RunAsync<T>(string key, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        var gate = _gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await action(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }
}
