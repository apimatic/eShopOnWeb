using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Subscription capability backed by Maxio Advanced Billing. Maxio is the system of record for
/// customers and subscriptions; eShopOnWeb users are correlated to Maxio customers through the
/// customer <c>reference</c> attribute.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans available for subscription in the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="customer"/> (looked up by
    /// <see cref="SubscriberProfile.Reference"/>) and subscribes them to <paramref name="planHandle"/>.
    /// The operation is idempotent: subscribing a customer that already holds a live subscription to
    /// the same plan returns that existing subscription.
    /// </summary>
    Task<SubscriptionPurchase> SubscribeAsync(SubscriberProfile customer, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all Maxio subscriptions for the customer identified by <paramref name="customerReference"/>.
    /// Returns an empty list when the customer does not exist yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPurchase>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
