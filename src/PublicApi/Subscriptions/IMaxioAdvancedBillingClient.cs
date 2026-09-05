using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioAdvancedBillingClient
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);
    Task<SubscriptionEnrollment> SubscribeAsync(MaxioCustomerInput customer, string planHandle, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(MaxioCustomerInput customer, CancellationToken cancellationToken = default);
}
