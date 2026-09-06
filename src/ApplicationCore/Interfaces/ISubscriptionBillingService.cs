using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, delegated to an external billing system of record.
/// This sits alongside - and is independent of - the one-time basket/order flow.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans a shopper may subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the shopper to a plan, creating the billing-system customer first if needed.
    /// Safe to call repeatedly: a shopper who already has a live subscription to the plan gets that
    /// subscription back rather than a second one.
    /// </summary>
    /// <param name="planHandle">The plan to subscribe to, or null to use the configured default plan.</param>
    Task<SubscriptionEnrollmentResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string? planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the shopper's subscriptions. Empty when they have no billing-system customer yet.</summary>
    Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
