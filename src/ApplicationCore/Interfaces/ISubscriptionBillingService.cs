using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscriptionDetails> SubscribeAsync(
        BillingIdentity identity,
        int productId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(
        BillingIdentity identity,
        CancellationToken cancellationToken);
}
