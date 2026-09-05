using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the "subscribe" hero flow on top of <see cref="IMaxioBillingClient"/>:
/// ensuring a Maxio customer exists for a buyer, enrolling them in a plan idempotently,
/// and reading back their subscriptions.
/// </summary>
public interface ISubscriptionEnrollmentService
{
    Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="buyer"/> and enrolls them in the
    /// plan identified by <paramref name="planHandle"/>. Idempotent: calling this repeatedly
    /// for the same buyer/plan returns the same subscription instead of creating duplicates.
    /// </summary>
    Task<(MaxioSubscription Subscription, bool AlreadyExisted)> SubscribeAsync(
        MaxioCustomerProfile buyer, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions belonging to the buyer, or an empty list if they have never subscribed.</summary>
    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForBuyerAsync(string buyerReference, CancellationToken cancellationToken = default);
}
