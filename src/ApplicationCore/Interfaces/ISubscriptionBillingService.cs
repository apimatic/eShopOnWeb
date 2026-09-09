using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record.
/// This capability is additive and parallel to the one-time Catalog/Basket/Order flow.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans a shopper can subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the given shopper to the plan identified by <paramref name="planHandle"/>.
    /// <para>
    /// Idempotent: ensures a billing customer exists for the shopper (keyed on
    /// <see cref="SubscriberIdentity.Reference"/>) and, if the shopper already has a live subscription to
    /// the same plan, returns it instead of creating a duplicate. A double-click never yields two
    /// customers or two subscriptions.
    /// </para>
    /// </summary>
    Task<SubscriptionResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions belonging to the given shopper (empty if they have none).</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
