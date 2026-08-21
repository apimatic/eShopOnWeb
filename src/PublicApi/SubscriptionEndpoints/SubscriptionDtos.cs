using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit)
{
    public static SubscriptionPlanDto From(SubscriptionPlan plan)
        => new(plan.Handle, plan.Name, plan.Description, plan.PriceInCents, plan.Interval, plan.IntervalUnit);
}

public sealed record SubscriptionDto(
    long Id,
    string Reference,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt)
{
    public static SubscriptionDto From(SubscriptionDetails subscription)
        => new(
            subscription.Id,
            subscription.Reference,
            subscription.ProductHandle,
            subscription.ProductName,
            subscription.PriceInCents,
            subscription.Interval,
            subscription.IntervalUnit,
            subscription.State,
            subscription.NextBillingAt);
}

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}
