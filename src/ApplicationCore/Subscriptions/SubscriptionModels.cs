using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <param name="Truncated">True when the plan list was cut short by the page cap; the list is then incomplete.</param>
public record SubscriptionPlanList(IReadOnlyList<SubscriptionPlan> Plans, bool Truncated);

public record SubscriptionSummary(
    int SubscriptionId,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    string? Reference);

/// <param name="AlreadySubscribed">True when the user already had this subscription and no new one was created.</param>
public record SubscribeResult(SubscriptionSummary Subscription, bool AlreadySubscribed);

public record SubscribeCommand(string UserName, string PlanHandle, string? FirstName, string? LastName);
