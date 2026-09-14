using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Recurring-subscription capability, backed by an external billing system of record.
/// </summary>
/// <remarks>
/// This runs alongside - and is independent of - the one-time Basket/Order checkout flow.
/// </remarks>
public interface ISubscriptionService
{
    /// <summary>Lists the plans a shopper can subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes <paramref name="subscriber"/> to <paramref name="planHandle"/>, creating the
    /// billing customer first if this is their first subscription.
    /// </summary>
    /// <param name="planHandle">
    /// Handle of the plan to subscribe to. When null or empty the configured default plan is used;
    /// if no default is configured the call fails rather than guessing.
    /// </param>
    /// <remarks>
    /// Idempotent per (subscriber, plan): repeating the call while a live subscription to the same
    /// plan exists returns that subscription instead of creating another one.
    /// </remarks>
    Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string? planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription the billing system holds for <paramref name="subscriber"/>.</summary>
    Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default);
}
