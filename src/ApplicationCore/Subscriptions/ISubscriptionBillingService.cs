using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionEnrollment> SubscribeAsync(
        ShopperBillingIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(
        ShopperBillingIdentity shopper,
        CancellationToken cancellationToken = default);
}
