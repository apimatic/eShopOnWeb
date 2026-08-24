using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing operations backed by the billing system of record (Maxio Advanced Billing).
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans available for subscription in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for the shopper and enrolls them in the given plan.
    /// Idempotent: repeating the call for the same shopper and plan returns the existing
    /// subscription instead of creating duplicates.
    /// </summary>
    Task<ShopperSubscription> SubscribeAsync(ShopperIdentity shopper, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the shopper's subscriptions. Returns an empty list if the shopper has never subscribed.</summary>
    Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsAsync(ShopperIdentity shopper, CancellationToken cancellationToken = default);
}
