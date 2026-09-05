using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Serializes enrolment attempts for one shopper in this API process. Maxio's
/// customer reference is unique and subscription references are deterministic,
/// so retries and restarts reconcile from Maxio before attempting a create.
/// </summary>
public sealed class UserSubscriptionCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<T> ExecuteAsync<T>(string userId, Func<Task<T>> action)
    {
        var gate = _locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }
}
