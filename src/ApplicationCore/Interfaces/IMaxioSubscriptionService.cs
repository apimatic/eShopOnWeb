using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing operations backed by Maxio Advanced Billing.
/// This is an additive capability that runs alongside the existing one-time commerce flow.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>Lists the plans a shopper can subscribe to (the configured product family).</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the user to the given plan. Idempotent end-to-end: ensures a single Maxio
    /// customer exists for the user, and never creates a second live subscription to a plan
    /// the user is already subscribed to.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the user's subscriptions as reported by Maxio.</summary>
    Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
