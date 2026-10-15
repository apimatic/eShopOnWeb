using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <summary>The plans on offer. <see cref="IsPartial"/> is true when listing stopped at the page cap.</summary>
public record SubscriptionPlanList(IReadOnlyList<SubscriptionPlan> Plans, bool IsPartial);

public record SubscriptionInfo(
    int SubscriptionId,
    string? PlanHandle,
    string? PlanName,
    string? State,
    long PriceInCents,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt);

public record SubscribeResult(SubscriptionInfo Subscription, bool AlreadySubscribed);
