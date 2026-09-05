using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the eShopOnWeb subscription-billing capability on top of Maxio Advanced
/// Billing: ensuring a Maxio customer exists for a buyer, enrolling them into a plan
/// idempotently, and reporting back their current subscriptions.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<MaxioProduct>> GetAvailablePlansAsync();

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="buyerReference"/> and enrolls them
    /// into <paramref name="planHandle"/>. Safe to call more than once for the same buyer/plan:
    /// if a live (non-canceled/expired) subscription to that plan already exists, it is
    /// returned instead of creating a duplicate.
    /// </summary>
    Task<SubscriptionEnrollmentResult> SubscribeAsync(string buyerReference, string email, string planHandle);

    /// <summary>
    /// Returns every Maxio subscription for the buyer, or an empty list if the buyer has never
    /// subscribed (i.e. no Maxio customer exists for them yet).
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForBuyerAsync(string buyerReference);
}
