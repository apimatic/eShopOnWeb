using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Serialises operator re-send attempts by caller-supplied idempotency key so that a repeat under the
/// same key never produces a second send. The claim must be established atomically — checked-and-acted
/// under the same guard — rather than checked and then acted on. Implemented as a process-wide guard
/// (this application runs as a single PublicApi host with a per-host store).
/// </summary>
public interface IResendIdempotencyGuard
{
    /// <summary>
    /// Run <paramref name="operation"/> under an exclusive claim on <paramref name="idempotencyKey"/>.
    /// Concurrent callers with the same key are serialised, so the operation can safely check for an
    /// existing result before creating a new one.
    /// </summary>
    Task<T> ExecuteAsync<T>(string idempotencyKey, Func<CancellationToken, Task<T>> operation, CancellationToken ct);
}
