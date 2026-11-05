using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for the subscriber and enrolls them in the plan.
    /// Idempotent: an existing live subscription to the same plan is returned instead of creating another.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default);
}
