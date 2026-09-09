using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing capability, backed by an external billing system of record
/// (Maxio Advanced Billing). This is an additive, parallel capability to the existing one-time
/// commerce flow and does not replace the Catalog/Basket/Order pipeline.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Returns the subscription plans a shopper can subscribe to (the products in the
    /// configured product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the given eShopOnWeb user to a plan. The operation is idempotent: it ensures a
    /// single billing customer exists for the user and will not create a duplicate live
    /// subscription to the same plan if one already exists (so a double-click is safe).
    /// </summary>
    /// <param name="userName">The eShopOnWeb user identity (from the caller's JWT).</param>
    /// <param name="planHandle">The stable handle of the plan to subscribe to.</param>
    Task<CustomerSubscription> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subscriptions currently associated with the given eShopOnWeb user.
    /// Returns an empty list if the user has never subscribed (no billing customer yet).
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken = default);
}
