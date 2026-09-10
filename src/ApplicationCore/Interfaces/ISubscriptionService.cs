using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application-facing abstraction over the recurring-subscription billing system
/// (Maxio Advanced Billing). This is an additive, parallel capability to the existing
/// one-time commerce flow and does not replace Basket/Order.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans that a shopper can subscribe to.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the identified shopper in a plan. The operation is idempotent: it ensures a
    /// single billing customer exists for the shopper and never creates a duplicate active
    /// subscription to the same plan, so a double-click is safe.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions currently on file for the identified shopper. Returns an empty
    /// list when the shopper has never subscribed (no billing customer exists yet).
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default);
}
