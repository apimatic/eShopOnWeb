using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public static class SubscriptionConstants
{
    /// <summary>Plan handle used when a subscribe request names no plan.</summary>
    public const string DefaultPlanHandle = "eshop-pro";
}

/// <summary>The signed-in shopper, as resolved from the token.</summary>
public record SubscriberIdentity(string UserId, string? Email, string? UserName);

public record SubscriptionPlan(
    string Handle,
    string Name,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool IsDefault);

/// <summary>IsPartial is true when the page cap was hit and more plans may exist.</summary>
public record SubscriptionPlanListing(IReadOnlyList<SubscriptionPlan> Plans, bool IsPartial);

public record SubscriptionInfo(
    int SubscriptionId,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CurrentPeriodEndsAt);

public record SubscribeResult(SubscriptionInfo Subscription, bool Created);
