using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionIdempotencyLock
{
    Task<T> ExecuteAsync<T>(string key, Func<Task<T>> action, CancellationToken cancellationToken = default);
}
