using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Serialises payment operations that target the same order, so a double-click cannot
/// authorize or capture the shopper twice. Dispose the returned handle to release.
/// </summary>
public interface IPaymentLock
{
    Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default);
}
