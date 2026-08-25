using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);
    Task<SubscriptionDetails> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(
        BillingUser user,
        CancellationToken cancellationToken = default);
}
