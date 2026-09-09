using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Subscription operations backed by Maxio Advanced Billing as the system of record.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the available subscription plans from the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given eShopOnWeb user, then subscribes
    /// them to the plan identified by <paramref name="productHandle"/>. Idempotent:
    /// returns the existing live subscription when the user already holds one.
    /// </summary>
    Task<SubscriptionDto> SubscribeAsync(string userId, string email, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the Maxio subscriptions held by the given eShopOnWeb user.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userId, string email, CancellationToken cancellationToken = default);
}
