using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface ISubscriptionBillingService
{
    /// <summary>Lists the purchasable plans in the configured Maxio product family.</summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the user and subscribes them to the plan.
    /// Idempotent: a repeat call (double-click, retry) returns the existing
    /// subscription instead of creating a second customer or subscription.
    /// </summary>
    Task<SubscriptionDto> SubscribeAsync(BillingUser user, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the user's subscriptions. Empty when the user has no Maxio customer yet.</summary>
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(BillingUser user, CancellationToken cancellationToken = default);
}
