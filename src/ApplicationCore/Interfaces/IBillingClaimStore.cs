using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Durable, atomic claims on billing writes. Every method is a single round-trip whose outcome the
/// store enforces (primary key / concurrency token), never a read-then-write.
/// </summary>
public interface IBillingClaimStore
{
    /// <summary>
    /// Inserts <paramref name="candidate"/>. If a claim with the same key already exists the store refuses
    /// the insert and the existing claim is returned with <c>Acquired = false</c>.
    /// </summary>
    Task<BillingClaimAttempt> TryClaimAsync(BillingClaim candidate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="change"/> to <paramref name="claim"/> and saves it only if nobody changed the
    /// claim since it was read. Returns false (and leaves the stored row untouched) when someone else did.
    /// </summary>
    Task<bool> TryUpdateAsync(BillingClaim claim, Action<BillingClaim> change, CancellationToken cancellationToken = default);

    /// <summary>Deletes the claim so the operation can be attempted again. A no-op if already gone.</summary>
    Task ReleaseAsync(BillingClaim claim, CancellationToken cancellationToken = default);

    Task<BillingClaim?> FindAsync(string key, CancellationToken cancellationToken = default);
}

public sealed record BillingClaimAttempt(bool Acquired, BillingClaim Claim);
