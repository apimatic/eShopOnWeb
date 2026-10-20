using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Stores the claims that keep a shopper from creating the same billing record twice.
/// </summary>
public interface IBillingClaimStore
{
    /// <summary>
    /// Inserts <paramref name="claim"/>. Returns <c>false</c> when the store refuses it because a claim with the
    /// same reference already exists; the refusal is atomic, so of two concurrent callers exactly one wins.
    /// </summary>
    Task<bool> TryAddAsync<TClaim>(TClaim claim, CancellationToken cancellationToken = default) where TClaim : BillingClaim;

    Task<TClaim?> GetAsync<TClaim>(string reference, CancellationToken cancellationToken = default) where TClaim : BillingClaim;

    /// <summary>Persists changes made to a claim returned by <see cref="GetAsync{TClaim}"/> or added by <see cref="TryAddAsync{TClaim}"/>.</summary>
    Task UpdateAsync<TClaim>(TClaim claim, CancellationToken cancellationToken = default) where TClaim : BillingClaim;

    /// <summary>Releases a claim so a later request can try again. Removing a claim that is already gone is not an error.</summary>
    Task RemoveAsync<TClaim>(TClaim claim, CancellationToken cancellationToken = default) where TClaim : BillingClaim;

    /// <summary>The buyer's subscription claims that are not <see cref="BillingClaimState.Completed"/>.</summary>
    Task<IReadOnlyList<SubscriptionClaim>> ListUnsettledSubscriptionClaimsAsync(string buyerId, CancellationToken cancellationToken = default);
}
