using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing against Maxio Advanced Billing.
/// Maxio is the system of record; this service does not persist mappings locally.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the shopper and enrolls them on the given plan.
    /// Idempotent: a double-submit never creates a second customer or a second live subscription
    /// for the same plan.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string? productHandle,
        CancellationToken cancellationToken = default);
}
