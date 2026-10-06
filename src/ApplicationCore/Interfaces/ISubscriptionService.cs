using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates recurring-subscription billing against Maxio Advanced Billing.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available in the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given reference and subscribes them to the given plan.
    /// Idempotent: an existing active subscription to the same plan is returned instead of creating a duplicate.
    /// </summary>
    Task<Subscription> SubscribeAsync(string customerReference, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the Maxio customer identified by the given reference.
    /// </summary>
    Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
