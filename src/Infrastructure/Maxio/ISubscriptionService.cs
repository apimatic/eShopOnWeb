using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// High-level subscription operations bridging eShopOnWeb users and the
/// Maxio Advanced Billing site (customers, product catalog, subscriptions).
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanView>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the user, then enrolls them in the
    /// given plan. Idempotent: repeated calls (double clicks) return the
    /// existing subscription instead of creating a second one.
    /// </summary>
    Task<UserSubscriptionView> SubscribeAsync(string username, string? planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// All Maxio subscriptions for the given user, empty when the user has no
    /// Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<UserSubscriptionView>> ListUserSubscriptionsAsync(string username, CancellationToken cancellationToken = default);
}