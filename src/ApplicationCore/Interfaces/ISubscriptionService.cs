using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription capability offered to eShopOnWeb shoppers. Runs alongside - and entirely
/// independently of - the one-time basket and order flow.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Plans a shopper can subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls <paramref name="subscriber"/> in the plan identified by <paramref name="planHandle"/>,
    /// creating the billing customer on first use. Repeating the call does not create a second
    /// customer or a second subscription.
    /// </summary>
    /// <param name="idempotencyKey">
    /// Optional caller-supplied token. When supplied, every call with the same key and subscriber
    /// returns the same subscription, even if the shopper cancelled it in the meantime.
    /// </param>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, string? idempotencyKey = null, CancellationToken cancellationToken = default);

    /// <summary>Every subscription held by <paramref name="subscriber"/>, newest first.</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
