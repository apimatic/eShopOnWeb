using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscriptionDetails> SubscribeAsync(
        BillingUser user,
        string planHandle,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(
        BillingUser user,
        CancellationToken cancellationToken);
}
