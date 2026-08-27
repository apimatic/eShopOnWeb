using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.PublicApi.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    string Currency,
    int Interval,
    string IntervalUnit)
{
    public static SubscriptionPlanDto From(BillingPlan plan) =>
        new(plan.Handle, plan.Name, plan.Description, plan.PriceInCents, plan.Currency, plan.Interval, plan.IntervalUnit);
}

public sealed record SubscriptionDto(
    int Id,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    string Currency,
    string State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CurrentPeriodEndsAt)
{
    public static SubscriptionDto From(BillingSubscription subscription) =>
        new(subscription.Id, subscription.ProductHandle, subscription.ProductName, subscription.PriceInCents,
            subscription.Currency, subscription.State, subscription.NextBillingDate, subscription.CurrentPeriodEndsAt);
}

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> Plans);
public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);
public sealed record CreateSubscriptionRequest(string ProductHandle);
public sealed record CreateSubscriptionResponse(SubscriptionDto Subscription);
