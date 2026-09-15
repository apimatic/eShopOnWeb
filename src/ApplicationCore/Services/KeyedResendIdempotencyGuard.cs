using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Per-key async mutex. Two requests carrying the same idempotency key run one at a time, so the
/// second sees the first's persisted result and short-circuits instead of sending again. Different
/// keys never contend with one another.
/// </summary>
public sealed class KeyedResendIdempotencyGuard : IResendIdempotencyGuard
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<T> RunExclusivelyAsync<T>(string key, Func<Task<T>> action)
    {
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}
