using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Shopper-facing subscription billing. Maxio Advanced Billing is the system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);

    Task<CustomerSubscription> SubscribeAsync(ShopperIdentity shopper, string? productHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(ShopperIdentity shopper, CancellationToken cancellationToken);
}
