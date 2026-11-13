using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Durable, atomic claims on billing-provider writes. Every mutating method is a single conditional write the
/// store enforces (primary key on insert, concurrency token on update/delete) - never a read-then-write check.
/// </summary>
public interface IBillingClaimStore
{
    /// <summary>Inserts the claim. Returns <c>false</c> when a claim with the same key already exists.</summary>
    Task<bool> TryInsertAsync(BillingClaim claim, CancellationToken cancellationToken = default);

    Task<BillingClaim?> FindAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="change"/> to the claim as loaded. Returns <c>false</c> (and persists nothing) when
    /// another caller changed or removed it since it was loaded.
    /// </summary>
    Task<bool> TryUpdateAsync(BillingClaim claim, Action<BillingClaim> change, CancellationToken cancellationToken = default);

    /// <summary>Removes the claim as loaded. Returns <c>false</c> when another caller changed or removed it first.</summary>
    Task<bool> TryReleaseAsync(BillingClaim claim, CancellationToken cancellationToken = default);
}
