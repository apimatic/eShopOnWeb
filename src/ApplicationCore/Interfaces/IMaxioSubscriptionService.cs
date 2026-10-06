using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Subscription billing capability backed by Maxio Advanced Billing (system of record).
/// </summary>
public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<MaxioProduct>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeOutcome> SubscribeAsync(ShopperIdentity shopper, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForShopperAsync(ShopperIdentity shopper, CancellationToken cancellationToken = default);
}
