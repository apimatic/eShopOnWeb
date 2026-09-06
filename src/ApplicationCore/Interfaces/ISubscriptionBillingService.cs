using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, delegated to an external billing system of record.
/// Every member throws <see cref="Exceptions.BillingException"/> — and nothing else — when the billing
/// system cannot satisfy the request.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans a shopper may subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls <paramref name="subscriber"/> in the plan identified by <paramref name="planHandle"/>.
    /// Idempotent: repeated calls (a double-clicked button, a retried request) return the existing live
    /// subscription rather than creating a second customer or a second subscription.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions currently held by <paramref name="subscriber"/>. Returns an empty list when
    /// the shopper has never subscribed — it never creates a customer as a side effect.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
