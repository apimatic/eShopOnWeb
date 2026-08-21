using System;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string ProductHandle,
    string? PricePointHandle,
    string Name,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit)
{
    public static SubscriptionPlanDto From(SubscriptionPlan plan) =>
        new(
            plan.ProductHandle,
            plan.PricePointHandle,
            plan.Name,
            plan.PriceInCents,
            plan.Interval,
            plan.IntervalUnit);
}

public sealed record SubscriptionDto(
    int Id,
    string ProductHandle,
    string? ProductName,
    string? PricePointHandle,
    long? PriceInCents,
    string? Currency,
    string? State,
    DateTimeOffset? NextBillingDate)
{
    public static SubscriptionDto From(SubscriptionDetails subscription) =>
        new(
            subscription.Id,
            subscription.ProductHandle,
            subscription.ProductName,
            subscription.PricePointHandle,
            subscription.PriceInCents,
            subscription.Currency,
            subscription.State,
            subscription.NextBillingDate);
}

public sealed record CreateSubscriptionRequest(string ProductHandle, string? PricePointHandle);
