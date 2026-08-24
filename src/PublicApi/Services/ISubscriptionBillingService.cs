using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Services;

/// <summary>
/// Orchestrates subscription billing with Maxio Advanced Billing as the system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans available in the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the given user to a plan. Idempotent: ensures the Maxio customer exists
    /// (at most one per user) and returns the existing subscription when the user already
    /// has a live subscription to the same plan.
    /// </summary>
    Task<SubscriptionDto> SubscribeAsync(string username, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the given user's subscriptions as recorded in Maxio.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string username, CancellationToken cancellationToken = default);
}
