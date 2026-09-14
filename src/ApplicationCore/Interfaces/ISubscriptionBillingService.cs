using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Fronts the recurring-billing provider (Maxio Advanced Billing). Implementations must make
/// <see cref="SubscribeAsync"/> idempotent per <paramref name="buyerId"/> - repeat calls for the
/// same buyer must reuse the same billing-provider customer rather than creating duplicates.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken ct = default);

    Task<Subscription> SubscribeAsync(string buyerId, string planHandle, CancellationToken ct = default);

    Task<IReadOnlyList<Subscription>> GetSubscriptionsForBuyerAsync(string buyerId, CancellationToken ct = default);
}
