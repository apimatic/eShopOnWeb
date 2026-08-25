using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Billing system of record for recurring subscriptions (Maxio Advanced Billing).
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the subscribable plans in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the shopper and enrolls them in the given plan.
    /// Idempotent: a repeated call for the same shopper and plan returns the existing
    /// subscription instead of creating a duplicate.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(ShopperInfo shopper, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions the shopper holds in Maxio. Empty when the shopper has
    /// no Maxio customer record yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(ShopperInfo shopper, CancellationToken cancellationToken = default);
}
