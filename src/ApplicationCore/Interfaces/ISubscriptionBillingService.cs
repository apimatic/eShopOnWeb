using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing capability, backed by the billing system of record (Maxio Advanced
/// Billing). This is an additive, parallel capability to the existing one-time commerce flow.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans a shopper can subscribe to (products in the configured product family).</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls <paramref name="subscriber"/> in a plan. Ensures a billing customer exists for the
    /// eShopOnWeb user (creating one if needed) and creates the subscription. The whole operation is
    /// idempotent: a repeated request (e.g. a double-click) never creates a second customer or a
    /// second live subscription to the same plan.
    /// </summary>
    /// <param name="planHandle">
    /// The Maxio product handle to subscribe to. When null/blank the first available plan is used.
    /// </param>
    Task<CustomerSubscription> SubscribeAsync(BillingSubscriber subscriber, string? planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions that belong to <paramref name="subscriber"/>.</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForAsync(BillingSubscriber subscriber, CancellationToken cancellationToken = default);
}
