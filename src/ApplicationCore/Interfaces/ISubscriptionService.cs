using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription capability, backed by an external billing system of record.
/// Parallel to — and independent of — the one-time catalog/basket/order flow.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the plans a shopper may subscribe to, from the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls <paramref name="subscriber"/> on a plan, creating the billing customer record if
    /// this is their first subscription.
    /// </summary>
    /// <remarks>
    /// Idempotent: concurrent or repeated calls for the same subscriber and plan resolve to a
    /// single billing customer and a single subscription.
    /// </remarks>
    Task<SubscribeResult> SubscribeAsync(
        Subscriber subscriber,
        SubscribeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions currently held by <paramref name="subscriber"/>. Returns an empty
    /// list — and creates nothing — when the subscriber has never subscribed.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        Subscriber subscriber,
        CancellationToken cancellationToken = default);
}
