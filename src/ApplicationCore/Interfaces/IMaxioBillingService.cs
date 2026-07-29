using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing for eShopOnWeb, backed by Maxio Advanced Billing as the
/// system of record. This is an additive capability that runs in parallel with the existing
/// one-time commerce (Catalog → Basket → Order) flow.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the plans a shopper can subscribe to (the products in the configured Maxio product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the eShopOnWeb user into a plan, ensuring a backing Maxio customer exists first.
    /// The operation is idempotent per user: concurrent or repeated calls (e.g. a double-click)
    /// never create a second customer or a duplicate subscription for the same plan.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions currently held by the eShopOnWeb user. Returns an empty list when
    /// the user has no backing Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);
}
