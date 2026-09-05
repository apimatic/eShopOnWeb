using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by the Maxio Advanced Billing sandbox site
/// (system of record for plans, customers and subscriptions). This is additive to, and
/// entirely independent from, the existing one-time Catalog/Basket/Order checkout flow.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans available for subscription, sourced live from the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given buyer and enrolls them in the given plan.
    /// Idempotent: if the buyer already has a non-canceled subscription to the same plan, that
    /// existing subscription is returned (with WasCreated = false) rather than creating a duplicate.
    /// </summary>
    Task<(Subscription Subscription, bool WasCreated)> SubscribeAsync(string buyerEmail, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the given buyer's subscriptions. Returns an empty list if the buyer has no Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<Subscription>> GetSubscriptionsForBuyerAsync(string buyerEmail, CancellationToken cancellationToken = default);
}
