using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.MaxioBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Subscription billing capability backed by Maxio Advanced Billing, the billing system of record.
/// All calls carry eShop identity via stable references (handles and app-generated references),
/// never Maxio numeric ids.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the subscription plans this application offers: the products of the configured
    /// Maxio product family. <see cref="PlanCatalog.Truncated"/> reports that the page cap cut
    /// the walk short.
    /// </summary>
    Task<PlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the eShop user (idempotent — one customer per user id),
    /// then subscribes that customer to the given plan handle (idempotent per plan — a repeat call
    /// for a live subscription returns the existing subscription). The plan handle must be one of
    /// the plans returned by <see cref="GetPlansAsync"/>.
    /// </summary>
    Task<SubscriptionInfo> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the Maxio subscriptions belonging to the eShop user's Maxio customer, read live from
    /// the billing system. Empty when the user has no Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionInfo>> GetSubscriptionsForShopperAsync(ShopperIdentity shopper, CancellationToken cancellationToken = default);
}