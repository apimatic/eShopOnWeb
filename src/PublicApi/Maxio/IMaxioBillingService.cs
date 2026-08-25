using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently subscribes the shopper to a plan: finds-or-creates the Maxio customer
    /// (by deterministic reference), then finds-or-creates the subscription. A retried or
    /// double-clicked call returns the existing subscription.
    /// </summary>
    Task<SubscriptionDto> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ShopperIdentity shopper, CancellationToken cancellationToken = default);
}
