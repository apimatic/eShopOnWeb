using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Insert-or-fail storage for <see cref="BillingClaim"/> rows.
/// </summary>
public interface IBillingClaimStore
{
    /// <summary>Inserts the claim. Returns false when a claim for the same user already exists.</summary>
    Task<bool> TryInsertAsync<T>(T claim, CancellationToken cancellationToken) where T : BillingClaim;

    Task<T?> GetAsync<T>(string userKey, CancellationToken cancellationToken) where T : BillingClaim;

    /// <summary>
    /// Applies <paramref name="mutate"/> and saves. Returns false when another request changed the claim
    /// since it was read (optimistic concurrency).
    /// </summary>
    Task<bool> TryUpdateAsync<T>(T claim, Action<T> mutate, CancellationToken cancellationToken) where T : BillingClaim;

    /// <summary>Deletes the claim so the user can try again. Returns false when another request changed it first.</summary>
    Task<bool> TryReleaseAsync<T>(T claim, CancellationToken cancellationToken) where T : BillingClaim;
}
