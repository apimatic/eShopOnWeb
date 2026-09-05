using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);
    Task<SubscriptionSummary> SubscribeAsync(SubscriptionSubscriber subscriber, string productHandle, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(SubscriptionSubscriber subscriber, CancellationToken cancellationToken = default);
}
