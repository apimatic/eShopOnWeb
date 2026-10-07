using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Subscribes the user to a plan. Idempotent: repeated calls for the same
    /// user and plan never create duplicate billing customers or subscriptions.
    /// </summary>
    Task<UserSubscription> SubscribeAsync(string userId, string email, string fullName,
        string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserSubscription>> GetSubscriptionsForUserAsync(string userId,
        CancellationToken cancellationToken);
}
