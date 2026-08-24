using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Identifies the authenticated eShopOnWeb shopper in calls to the billing service.
/// </summary>
/// <param name="UserId">The eShopOnWeb identity user id; used as the Maxio customer reference.</param>
/// <param name="Email">The shopper's email address.</param>
/// <param name="UserName">The shopper's username.</param>
public record ShopperInfo(string UserId, string Email, string UserName);

public record SubscribeResult(SubscriptionDto Subscription, bool Created);

public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans available in the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the shopper to a plan. Idempotent: the Maxio customer is looked up (or created once)
    /// by the eShopOnWeb user id, and an already-active subscription to the same plan is returned
    /// instead of creating a duplicate.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(ShopperInfo shopper, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the shopper's subscriptions. Returns an empty list when the shopper has no Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(ShopperInfo shopper, CancellationToken cancellationToken = default);
}
