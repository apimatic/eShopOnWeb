using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingClaimAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Durable store for <see cref="BillingClaim"/>s. Claims are inserted by primary key, so a second
/// insert for the same reference is refused by the store itself, not by a read-before-write.
/// </summary>
public interface IBillingClaimStore
{
    /// <summary>
    /// Inserts <paramref name="claim"/>. Returns <c>true</c> when this caller now holds the claim,
    /// <c>false</c> when the store refused it because a claim with the same reference already exists.
    /// </summary>
    Task<bool> TryInsertAsync<TClaim>(TClaim claim, CancellationToken cancellationToken = default)
        where TClaim : BillingClaim;

    /// <summary>Loads the claim for <paramref name="reference"/> (tracked, for a later update).</summary>
    Task<TClaim?> FindAsync<TClaim>(string reference, CancellationToken cancellationToken = default)
        where TClaim : BillingClaim;

    /// <summary>
    /// Saves changes made to a loaded claim. Returns <c>false</c> when another caller changed the
    /// same claim first (optimistic concurrency), in which case this caller must not proceed.
    /// </summary>
    Task<bool> TryUpdateAsync<TClaim>(TClaim claim, CancellationToken cancellationToken = default)
        where TClaim : BillingClaim;

    /// <summary>Removes a claim (release after the provider definitively refused the write).</summary>
    Task ReleaseAsync<TClaim>(TClaim claim, CancellationToken cancellationToken = default)
        where TClaim : BillingClaim;
}
