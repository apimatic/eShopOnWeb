using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<PlanCatalog> ListPlansAsync(Guid correlationId, CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(ShopperProfile shopper, string? planHandle, Guid correlationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ShopperSubscription>> ListMineAsync(string userId, Guid correlationId, CancellationToken cancellationToken);
}
