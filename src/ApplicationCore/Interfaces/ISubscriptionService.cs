using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SubscriptionPlan(string Handle, string Name, string? Description, long PriceInCents, int? Interval, string? IntervalUnit);

public record SubscriptionPlanList(IReadOnlyList<SubscriptionPlan> Plans, bool IsTruncated);

public record SubscriptionInfo(
    int? SubscriptionId,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? NextAssessmentAt);

public record SubscribeResult(SubscriptionInfo Subscription, bool AlreadySubscribed);

public interface ISubscriptionService
{
    Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(string userId, string userName, string? email, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionInfo>> ListMySubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}
