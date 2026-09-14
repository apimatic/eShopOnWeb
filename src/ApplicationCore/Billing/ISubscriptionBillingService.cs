using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);
    Task<SubscriptionSummary> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
