using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(string userId, string email, string? displayName, string productHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default);
}
