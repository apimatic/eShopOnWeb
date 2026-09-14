using System;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string ProductHandle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod)
{
    public static SubscriptionPlanDto From(BillingPlan plan) =>
        new(plan.Handle, plan.Name, plan.Description, plan.PriceInCents, plan.Interval, plan.IntervalUnit, plan.RequiresPaymentMethod);
}

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public sealed record SubscriptionDto(
    long SubscriptionId,
    string ProductHandle,
    string PlanName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt)
{
    public static SubscriptionDto From(BillingSubscription subscription) =>
        new(
            subscription.Id,
            subscription.ProductHandle,
            subscription.ProductName,
            subscription.PriceInCents,
            subscription.Interval,
            subscription.IntervalUnit,
            subscription.State,
            subscription.NextBillingAt);
}
