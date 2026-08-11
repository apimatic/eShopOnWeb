using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Serializes payment operations that target the same order so a double-click can never authorize,
/// capture or refund twice. Combined with the persisted order state, this makes every payment
/// operation idempotent in effect. (In a multi-instance deployment this would be backed by a
/// distributed lock or the database's optimistic concurrency token; within a single host a
/// process-wide keyed lock is sufficient and is what the in-memory run uses.)
/// </summary>
public interface IPaymentConcurrencyGuard
{
    Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default);
}
