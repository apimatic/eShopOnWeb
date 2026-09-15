using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Coalesces same-process duplicate enrollments while the database unique index protects durable state.</summary>
public sealed class SubscriptionEnrollmentCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<T> RunAsync<T>(string key, Func<Task<T>> operation)
    {
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
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
