using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing against Maxio Advanced Billing, the system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the shopper and enrolls them on the given plan.
    /// Idempotent: a repeated subscribe for the same live plan returns the existing subscription.
    /// </summary>
    Task<SubscriptionStatus> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionStatus>> ListSubscriptionsAsync(ShopperIdentity shopper, CancellationToken cancellationToken = default);
}
