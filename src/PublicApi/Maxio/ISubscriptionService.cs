using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Application-facing subscription service backed by Maxio Advanced Billing.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans (products) available in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanInfo>> ListSubscriptionPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="loginName"/> (idempotent, keyed on the
    /// customer reference) and subscribes that customer to the plan identified by
    /// <paramref name="planHandle"/>. A second live subscription to the same plan is never created:
    /// if one already exists the existing subscription is returned with <see cref="SubscribeResult.Created"/>
    /// set to false.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string loginName, string planHandle, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the caller's subscriptions. Returns an empty collection when the caller has no
    /// Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionInfo>> ListMySubscriptionsAsync(string loginName, CancellationToken cancellationToken);
}
