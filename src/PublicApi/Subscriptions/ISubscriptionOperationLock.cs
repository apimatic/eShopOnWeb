using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionOperationLock
{
    ValueTask<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken);
}
