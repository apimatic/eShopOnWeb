using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing for eShopOnWeb, backed by Maxio Advanced Billing as the system of record.
/// This capability is additive and parallel to the existing one-time commerce flow.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available to shoppers (the products in the configured Maxio product family).
    /// </summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls a shopper in a plan. Ensures a Maxio customer exists for the shopper (idempotent by reference)
    /// and creates the subscription, returning an equivalent active subscription if one already exists so that
    /// a repeated/double-clicked request never creates two customers or subscriptions.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to a shopper, identified by their stable reference. Returns an empty
    /// collection when no Maxio customer exists yet for the shopper.
    /// </summary>
    Task<IReadOnlyCollection<ShopperSubscription>> GetSubscriptionsAsync(
        string shopperReference, CancellationToken cancellationToken = default);
}
