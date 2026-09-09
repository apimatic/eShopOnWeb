using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingSubscription;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing. This is an additive,
/// parallel capability to the one-time Catalog/Basket/Order flow — it does not replace it.
///
/// Implementations must be idempotent with respect to the shopper: ensuring a customer exists or
/// enrolling them twice (e.g. from a double-click) must never create a second customer or a second
/// live subscription to the same plan.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the plans a shopper can subscribe to (the products in the configured Maxio product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given eShopOnWeb user and enrolls them in the plan
    /// identified by <paramref name="planHandle"/>. Idempotent: a repeat call returns the existing
    /// live subscription rather than creating another.
    /// </summary>
    /// <param name="userName">The eShopOnWeb user's stable identity (their login/email, taken from the JWT).</param>
    /// <param name="planHandle">The stable handle of the plan to subscribe to (e.g. "eshop-pro").</param>
    Task<SubscriptionEnrollmentResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subscriptions Maxio holds for the given eShopOnWeb user. Empty when the user has
    /// never subscribed (no Maxio customer yet).
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken = default);
}
