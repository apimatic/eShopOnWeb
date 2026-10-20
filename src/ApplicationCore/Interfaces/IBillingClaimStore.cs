using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Durable claims that keep a billing write from being requested twice. Every write to the billing
/// provider follows: claim, then provider call, then record the result.
/// </summary>
public interface IBillingClaimStore
{
    /// <summary>
    /// Inserts the claim. Returns <c>false</c> when the store refuses it because a claim with the same
    /// key already exists (the second caller of a double-submit or race).
    /// </summary>
    Task<bool> TryClaimCustomerAsync(BillingCustomerLink claim, CancellationToken cancellationToken = default);

    Task<BillingCustomerLink?> FindCustomerLinkAsync(string customerReference, CancellationToken cancellationToken = default);

    /// <summary>Saves a mutated claim. Returns <c>false</c> when another caller changed or removed it first.</summary>
    Task<bool> UpdateCustomerLinkAsync(BillingCustomerLink link, CancellationToken cancellationToken = default);

    /// <summary>Removes a claim the provider refused, or one proven never to have landed. Returns <c>false</c> when another caller changed or removed it first.</summary>
    Task<bool> ReleaseCustomerClaimAsync(BillingCustomerLink link, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the claim. Returns <c>false</c> when the store refuses it because a claim with the same
    /// key already exists (the second caller of a double-submit or race).
    /// </summary>
    Task<bool> TryClaimSubscriptionAsync(SubscriptionClaim claim, CancellationToken cancellationToken = default);

    Task<SubscriptionClaim?> FindSubscriptionClaimAsync(string customerReference, CancellationToken cancellationToken = default);

    /// <summary>Saves a mutated claim. Returns <c>false</c> when another caller changed or removed it first.</summary>
    Task<bool> UpdateSubscriptionClaimAsync(SubscriptionClaim claim, CancellationToken cancellationToken = default);

    /// <summary>Removes a claim the provider refused, or one proven never to have landed. Returns <c>false</c> when another caller changed or removed it first.</summary>
    Task<bool> ReleaseSubscriptionClaimAsync(SubscriptionClaim claim, CancellationToken cancellationToken = default);
}
