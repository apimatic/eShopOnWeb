using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDetails> SubscribeAsync(
        string userId,
        string userName,
        string email,
        string productHandle,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken);
}

