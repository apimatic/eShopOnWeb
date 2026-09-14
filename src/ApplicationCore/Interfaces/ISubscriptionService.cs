using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(SubscriptionSubscriber subscriber, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionEnrollment>> ListSubscriptionsAsync(SubscriptionSubscriber subscriber, CancellationToken cancellationToken = default);
}
