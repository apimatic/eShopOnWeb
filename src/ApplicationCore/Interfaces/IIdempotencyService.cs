using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A durable, atomic claim on a mutating operation. The claim is a persisted row keyed by the claim
/// string; the store's primary-key uniqueness rejects a second insert of the same key at save time.
/// This is the guard against a concurrent duplicate request — not an in-process lock and not a
/// read-before-write.
/// </summary>
public interface IIdempotencyService
{
    /// <summary>
    /// Atomically claims <paramref name="key"/>. Returns <c>true</c> if this caller acquired the claim,
    /// <c>false</c> if it already existed (the operation is a duplicate / already in progress).
    /// </summary>
    Task<bool> TryClaimAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Releases a previously-acquired claim so a later attempt can proceed. Called when the guarded
    /// operation failed or its outcome is unknown — a claim with no release turns one transient failure
    /// into a permanent refusal.
    /// </summary>
    Task ReleaseAsync(string key, CancellationToken ct = default);
}
