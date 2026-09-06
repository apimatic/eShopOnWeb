using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription capability, backed by an external billing system of record.
/// Implementations must be idempotent: repeating <see cref="SubscribeAsync"/> for the same shopper and
/// plan must not create a second billing customer or a second subscription.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Plans the shopper may subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for <paramref name="account"/> and enrolls it in
    /// <paramref name="planHandle"/>.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscriberAccount account, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// The shopper's subscriptions, newest first. Returns an empty list when the shopper has never
    /// subscribed (i.e. has no billing customer yet).
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(SubscriberAccount account, CancellationToken cancellationToken = default);
}
