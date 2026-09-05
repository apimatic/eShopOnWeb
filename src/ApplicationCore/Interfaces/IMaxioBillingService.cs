using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Subscription billing capability backed by Maxio Advanced Billing (the system of record for
/// recurring subscriptions). This is additive to, and independent of, the existing
/// Catalog/Basket/Order one-time-commerce flow.
/// </summary>
public interface IMaxioBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the buyer and enrolls them in the requested plan.
    /// Idempotent: repeating the same (buyer, plan) request never creates duplicate customers or
    /// subscriptions - the existing customer/subscription is returned instead.
    /// </summary>
    Task<SubscriberSubscription> SubscribeAsync(SubscriptionEnrollmentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// All of a buyer's subscriptions. Returns an empty list if the buyer has no Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<SubscriberSubscription>> GetSubscriptionsForBuyerAsync(string buyerReference, CancellationToken cancellationToken = default);
}
