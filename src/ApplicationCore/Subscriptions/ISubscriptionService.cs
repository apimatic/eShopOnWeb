using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionResult?> SubscribeAsync(
        BillingCustomerIdentity identity,
        string productHandle,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsAsync(
        BillingCustomerIdentity identity,
        CancellationToken cancellationToken);
}
