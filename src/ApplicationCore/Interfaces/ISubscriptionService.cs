using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <param name="IsComplete">false when the plan list was cut short by the paging cap.</param>
public record SubscriptionPlanList(IReadOnlyList<SubscriptionPlan> Plans, bool IsComplete);

public record SubscriptionSummary(
    int SubscriptionId,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? CreatedAt);

/// <summary>The authenticated shopper, as known from the JWT (UserName) and the identity store (Email).</summary>
public record Subscriber(string UserName, string Email);

public record SubscribeResult(SubscriptionSummary Subscription, bool AlreadySubscribed);

public interface ISubscriptionService
{
    Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionSummary>> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken);
}
