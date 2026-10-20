using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public record SubscriptionPlan(string Handle, string Name, string? Description, long PriceInCents, int? Interval, string? IntervalUnit);

public record SubscriptionPlanList(IReadOnlyList<SubscriptionPlan> Plans, bool IsTruncated);

public record SubscriptionSummary(
    string PlanHandle,
    string? PlanName,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    string? State,
    DateTimeOffset? NextBillingAt);

public record SubscribeResult(SubscriptionSummary Subscription, bool AlreadySubscribed);

public interface ISubscriptionService
{
    /// <summary>Plans of the configured Maxio product family (non-archived).</summary>
    Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>Idempotent: a repeat for the same user returns the existing subscription.</summary>
    Task<SubscribeResult> SubscribeAsync(string userId, string email, string displayName, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(string userId, CancellationToken cancellationToken);
}
