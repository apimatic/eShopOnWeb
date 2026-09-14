using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionOperationLock
{
    ValueTask<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken);
}
