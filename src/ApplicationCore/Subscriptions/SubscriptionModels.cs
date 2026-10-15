using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>A plan the shopper can subscribe to.</summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    int? Interval,
    string? IntervalUnit,
    bool TrialAvailable,
    bool SetupFee);

/// <summary>The plans on offer. <see cref="Truncated"/> is true when the page cap cut the walk short.</summary>
public record PlanListResult(IReadOnlyList<SubscriptionPlan> Plans, bool Truncated);

/// <summary>A subscription as reflected back to the shopper.</summary>
public record SubscriptionInfo(
    int? SubscriptionId,
    string? PlanHandle,
    string? PlanName,
    decimal? Price,
    string? State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CreatedAt);

/// <summary>The caller's subscriptions. <see cref="Truncated"/> is true when the page cap cut the walk short.</summary>
public record MySubscriptionsResult(IReadOnlyList<SubscriptionInfo> Subscriptions, bool Truncated);

/// <summary><see cref="AlreadySubscribed"/> is true when an earlier request had already created this subscription.</summary>
public record SubscribeResult(SubscriptionInfo Subscription, bool AlreadySubscribed);
