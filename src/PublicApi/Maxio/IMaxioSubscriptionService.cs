using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlansEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Orchestrates the eShopOnWeb subscription capability against Maxio Advanced
/// Billing: browsing plans, subscribing (idempotently) and listing a shopper's
/// subscriptions.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available in the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the eShopOnWeb user (idempotent) and
    /// subscribes them to the given product. If the user already has a live
    /// subscription to the product, the existing subscription is returned instead
    /// of creating a duplicate.
    /// </summary>
    Task<SubscriptionDto> SubscribeAsync(string userId, string email, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the Maxio subscriptions belonging to the eShopOnWeb user.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}
