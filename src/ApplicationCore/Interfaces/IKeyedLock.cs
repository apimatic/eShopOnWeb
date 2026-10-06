using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IKeyedLock
{
    Task<IDisposable> AcquireWaitAsync(string key, CancellationToken cancellationToken = default);
}
