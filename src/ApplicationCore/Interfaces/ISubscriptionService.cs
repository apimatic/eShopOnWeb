using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring subscription billing, backed by an external billing provider which is the system of
/// record for customers, plans and subscriptions.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the plans a shopper may subscribe to.
    /// </summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes a shopper to a plan, creating the billing customer on first use.
    /// The operation is idempotent per shopper and plan: repeating it while the shopper still holds
    /// the subscription returns the existing one instead of creating a second.
    /// </summary>
    /// <param name="subscriber">The shopper, as identified by the authenticated caller.</param>
    /// <param name="planHandle">
    /// The handle of the plan to subscribe to. When null, and exactly one plan is published, that
    /// plan is used.
    /// </param>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string? planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions the shopper holds, most recently created first. Returns an empty
    /// collection when the shopper has never subscribed.
    /// </summary>
    Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
