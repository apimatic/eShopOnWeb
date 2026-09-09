using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

/// <summary>
/// A plan (Maxio product) available for subscription.
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public long PriceInCents { get; init; }

    public int Interval { get; init; }

    public string? IntervalUnit { get; init; }

    public bool RequireCreditCard { get; init; }
}

/// <summary>
/// A subscription (enrollment) for an eShopOnWeb user, as recorded by Maxio.
/// </summary>
public class SubscriptionInfo
{
    public int MaxioSubscriptionId { get; init; }

    public string PlanHandle { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    public long PriceInCents { get; init; }

    public string State { get; init; } = string.Empty;

    public System.DateTime? NextBillingAt { get; init; }

    public System.DateTime? ActivatedAt { get; init; }

    public System.DateTime CreatedAtUtc { get; init; }

    /// <summary>
    /// True when the values were refreshed from Maxio during this request.
    /// </summary>
    public bool Live { get; init; }

    /// <summary>
    /// True when the subscription already existed and no new one was created.
    /// </summary>
    public bool AlreadySubscribed { get; init; }
}

/// <summary>
/// Enrollment and query operations for Maxio-backed subscriptions,
/// scoped to the calling eShopOnWeb user.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists the plans (Maxio products) available in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the user (idempotent) and subscribes
    /// them to the given plan. When <paramref name="planHandle"/> is null the
    /// first active plan of the family is used. Safe against double-clicks:
    /// an existing live subscription to the same plan is returned instead of
    /// creating a duplicate.
    /// </summary>
    Task<SubscriptionInfo> SubscribeAsync(ApplicationUser user, string? planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the user's subscriptions, enriched with live state from Maxio when available.
    /// </summary>
    Task<IReadOnlyList<SubscriptionInfo>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken = default);
}
