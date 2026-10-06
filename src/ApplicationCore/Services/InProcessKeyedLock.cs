using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// In-process keyed semaphore. It only serializes work inside a single API instance;
/// the durable guarantee against duplicate customers is Maxio's unique customer reference.
/// </summary>
public class InProcessKeyedLock : IKeyedLock
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();

    public async Task<T> RunAsync<T>(string key, Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(key, nameof(key));
        Guard.Against.Null(action, nameof(action));

        var semaphore = _semaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            return await action();
        }
        finally
        {
            semaphore.Release();
        }
    }
}
