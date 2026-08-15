using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing as the system of record.
/// This is an additive capability parallel to the existing one-time commerce flow; it does not
/// touch the Basket/Order aggregates. Implementations own all Maxio SDK interaction.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans a shopper can subscribe to (the Maxio products in the configured
    /// product family), ordered by price ascending.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the given user to the plan identified by <paramref name="planHandle"/>.
    /// Idempotent: ensures a single Maxio customer exists for the user (keyed by
    /// <see cref="SubscriberIdentity.Reference"/>) and, if the user is already subscribed to that
    /// plan, returns the existing subscription instead of creating a second one.
    /// </summary>
    /// <exception cref="PlanNotFoundException">The plan handle does not exist in the configured product family.</exception>
    /// <exception cref="SubscriptionBillingException">Maxio rejected the request or is unreachable.</exception>
    Task<SubscriptionResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the given user's subscriptions as recorded by Maxio. Returns an empty list when the
    /// user has no Maxio customer record yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
