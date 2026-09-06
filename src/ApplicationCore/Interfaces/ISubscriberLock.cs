using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Serialises work for a single subscriber so two concurrent requests from the same shopper
/// cannot both decide that a customer or a subscription still needs creating.
/// </summary>
public interface ISubscriberLock
{
    /// <summary>Waits for exclusive access to <paramref name="key"/>. Dispose the result to release it.</summary>
    Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default);
}
