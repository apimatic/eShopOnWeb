using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <param name="Acquired">True when this caller holds the claim; false when someone else already did.</param>
/// <param name="Claim">The claim held (when acquired) or the pre-existing one (when not).</param>
public sealed record ClaimResult<T>(bool Acquired, T Claim) where T : class;

/// <summary>
/// Claims kept in the application's own store. A claim is an insert of its own, refused by the store's
/// primary key, taken BEFORE the billing-provider call.
/// </summary>
public interface IBillingClaimStore
{
    Task<ClaimResult<SubscriptionClaim>> TryClaimSubscriptionAsync(string userId, string planHandle, CancellationToken ct);
    Task CompleteSubscriptionAsync(string userId, string planHandle, int subscriptionId, CancellationToken ct);
    Task MarkSubscriptionUnknownAsync(string userId, string planHandle, CancellationToken ct);
    /// <summary>Deletes the claim, but only if it is still the one stamped <paramref name="claimedAtUtc"/>.</summary>
    Task ReleaseSubscriptionAsync(string userId, string planHandle, DateTime claimedAtUtc, CancellationToken ct);

    Task<ClaimResult<MaxioCustomerClaim>> TryClaimCustomerAsync(string userId, CancellationToken ct);
    Task CompleteCustomerAsync(string userId, int customerId, CancellationToken ct);
    Task ReleaseCustomerAsync(string userId, DateTime claimedAtUtc, CancellationToken ct);
}
