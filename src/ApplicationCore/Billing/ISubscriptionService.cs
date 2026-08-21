using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public interface ISubscriptionService
{
    Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken = default);
    Task<SubscribeResult> SubscribeAsync(string userId, string email, string productHandle, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}
