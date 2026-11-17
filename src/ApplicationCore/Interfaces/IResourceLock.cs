using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IResourceLock
{
    Task<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default);
}
