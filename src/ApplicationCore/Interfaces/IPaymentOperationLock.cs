using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Serializes payment operations that share a key (e.g. all operations on one order) within the
/// process, so a double-click cannot run two authorize/capture attempts concurrently. Combined with
/// the persisted payment state and PayPal-Request-Id idempotency keys, this makes the money-moving
/// operations idempotent in effect.
/// </summary>
public interface IPaymentOperationLock
{
    Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default);
}
