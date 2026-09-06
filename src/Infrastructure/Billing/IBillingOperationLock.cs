using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Serializes billing operations that must not run concurrently for the same subject.
/// </summary>
/// <remarks>
/// Maxio offers no idempotency key and no create-or-get subscription operation, so "does this shopper
/// already have a live subscription?" followed by "create one" is a check-then-act with a window between the
/// two calls. Closing that window is the application's job; this is the seam where it is closed.
/// </remarks>
public interface IBillingOperationLock
{
    /// <summary>
    /// Acquires exclusive access for <paramref name="key"/>. Dispose the result to release.
    /// </summary>
    Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default);
}
