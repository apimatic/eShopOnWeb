using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Serializes billing operations per eShopOnWeb user (customer reference) within this process so that
/// concurrent requests — e.g. a double-clicked "Subscribe" button — cannot race into creating two Maxio
/// customers or two subscriptions. Registered as a singleton. Cross-process safety is additionally
/// provided by Maxio's own unique customer reference and by the pre-create existing-subscription check.
/// </summary>
public sealed class MaxioIdempotencyGuard
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    /// <summary>Runs <paramref name="operation"/> while holding the per-key lock.</summary>
    public async Task<T> RunExclusiveAsync<T>(string key, Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await operation();
        }
        finally
        {
            gate.Release();
        }
    }
}
