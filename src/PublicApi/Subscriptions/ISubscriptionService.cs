using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>A subscribable plan/price as offered by the configured Maxio product family.</summary>
public sealed record SubscriptionPlanInfo(
    string Handle,
    string? Name,
    decimal? Price,
    int? Interval,
    string? IntervalUnit,
    bool? RequiresCreditCard);

/// <summary>A Maxio subscription summary for the signed-in shopper.</summary>
public sealed record SubscriptionInfo(
    int? MaxioSubscriptionId,
    string? ProductHandle,
    string? ProductName,
    decimal? Price,
    string? Currency,
    string? State,
    DateTimeOffset? CurrentPeriodStartedAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? CreatedAt);

public sealed record SubscribeResult(bool AlreadySubscribed, SubscriptionInfo Subscription);

/// <summary>
/// Application boundary in front of the Maxio Advanced Billing SDK.
/// All provider failures leave this boundary as <see cref="MaxioSubscriptionException"/>.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the subscribe plans in the configured product family (for catalog browsing).</summary>
    Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Idempotently ensures a Maxio customer exists for <paramref name="customerReference"/> and that the
    /// customer holds a live subscription to <paramref name="productHandle"/>. Creating one when absent.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string customerReference, string productHandle, CancellationToken cancellationToken);

    /// <summary>Lists the subscriptions held by the Maxio customer identified by <paramref name="customerReference"/>.</summary>
    Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken);
}
