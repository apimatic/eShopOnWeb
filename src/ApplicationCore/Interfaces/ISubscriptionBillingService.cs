using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record. This is the
/// application-facing contract; the wire details of the billing provider live in Infrastructure.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans a shopper can subscribe to.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the subscriber in the plan identified by <paramref name="planHandle"/>. Ensures a billing
    /// customer exists for the subscriber first, and is idempotent: if the subscriber already has a live
    /// subscription to the same plan, that existing subscription is returned rather than a second one created.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriber's subscriptions. Returns an empty collection if they have no billing customer yet.</summary>
    Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
