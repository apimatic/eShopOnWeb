using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing (the system of record).
/// Implementations must make subscribe idempotent so a double-click never creates a
/// duplicate customer or subscription.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>Lists the plans available to subscribe to (products in the configured family).</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the subscriber (idempotent) and enrolls them in the
    /// given plan. If the subscriber already holds a live subscription to that plan, that
    /// existing subscription is returned instead of creating a new one.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions held by the subscriber (empty if they have no Maxio customer yet).</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
