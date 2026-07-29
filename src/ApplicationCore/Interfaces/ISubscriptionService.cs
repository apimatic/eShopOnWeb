using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by the external billing system of record.
/// This is an additive capability that runs in parallel to the one-time commerce flow.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Plans a shopper can subscribe to (the products in the configured product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the shopper in a plan. Idempotent: ensures a single billing customer exists for
    /// the shopper and returns the existing active subscription for the plan if one is already
    /// present, so a double-click never creates duplicate customers or subscriptions.
    /// </summary>
    /// <param name="subscriber">The eShopOnWeb shopper, from their authenticated identity.</param>
    /// <param name="planHandle">The plan to subscribe to; when null, the configured default plan is used.</param>
    Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string? planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The shopper's subscriptions as reported by the billing system. Empty when the shopper
    /// has no billing customer yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
