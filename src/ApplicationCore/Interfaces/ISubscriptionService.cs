using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring subscription billing, delegated to an external billing system of record.
/// This capability runs in parallel with the one-time Catalog/Basket/Order flow.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Plans a shopper may subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the shopper on a plan, creating the billing customer first when needed.
    /// Implementations must be idempotent: repeating the call for a shopper who already has a
    /// live subscription to the same plan returns the existing subscription.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>All subscriptions the shopper holds in the billing system, newest first.</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default);
}
