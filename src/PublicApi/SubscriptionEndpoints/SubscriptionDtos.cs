using System;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    string? ProductPricePointHandle)
{
    public static SubscriptionPlanDto From(BillingPlan plan) =>
        new(
            plan.Handle,
            plan.Name,
            plan.Description,
            plan.PriceInCents,
            plan.Interval,
            plan.IntervalUnit,
            plan.ProductPricePointHandle);
}

public sealed record SubscriptionDto(
    int Id,
    string Reference,
    string ProductHandle,
    string ProductName,
    long? PriceInCents,
    string? Currency,
    string? State,
    DateTimeOffset? NextBillingAt)
{
    public static SubscriptionDto From(BillingSubscription subscription) =>
        new(
            subscription.Id,
            subscription.Reference,
            subscription.ProductHandle,
            subscription.ProductName,
            subscription.PriceInCents,
            subscription.Currency,
            subscription.State,
            subscription.NextBillingAt);
}

public sealed class CreateSubscriptionRequest
{
    public string? ProductHandle { get; init; }
}

public sealed record SubscriptionPendingResponse(string State, string StatusUrl);
