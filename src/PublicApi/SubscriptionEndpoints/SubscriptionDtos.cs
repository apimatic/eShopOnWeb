using System;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod)
{
    public static SubscriptionPlanDto From(SubscriptionPlan plan) => new(
        plan.Handle,
        plan.Name,
        plan.Description,
        plan.PriceInCents,
        plan.Interval,
        plan.IntervalUnit,
        plan.RequiresPaymentMethod);
}

public sealed record SubscriptionDto(
    long Id,
    string PlanHandle,
    string PlanName,
    string PricePointName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt)
{
    public static SubscriptionDto From(SubscriptionSummary subscription) => new(
        subscription.Id,
        subscription.PlanHandle,
        subscription.PlanName,
        subscription.PricePointName,
        subscription.PriceInCents,
        subscription.Interval,
        subscription.IntervalUnit,
        subscription.State,
        subscription.NextBillingAt);
}
