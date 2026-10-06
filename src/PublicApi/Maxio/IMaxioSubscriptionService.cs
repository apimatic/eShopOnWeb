using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Subscription billing operations against Maxio Advanced Billing for an
/// authenticated eShopOnWeb user (identified by username/email).
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>Lists subscribable plans from the configured Maxio product family.</summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default);

    /// <summary>
    /// Enrolls the user into the plan identified by <paramref name="planHandle"/>.
    /// Idempotent: enrolling twice for the same plan returns the existing subscription
    /// with <see cref="SubscriptionDto.AlreadySubscribed"/> set.
    /// </summary>
    Task<SubscriptionDto> SubscribeAsync(string username, string planHandle, CancellationToken ct = default);

    /// <summary>Lists the user's subscriptions; empty when the user has no Maxio customer yet.</summary>
    Task<IReadOnlyList<SubscriptionDto>> ListForUserAsync(string username, CancellationToken ct = default);
}