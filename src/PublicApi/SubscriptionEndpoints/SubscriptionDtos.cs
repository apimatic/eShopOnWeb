using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    decimal Price,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod)
{
    public static SubscriptionPlanDto From(SubscriptionPlan plan) => new(
        plan.Handle,
        plan.Name,
        plan.Description,
        plan.PriceInCents,
        plan.PriceInCents / 100m,
        plan.Interval,
        plan.IntervalUnit,
        plan.RequiresPaymentMethod);
}

public sealed record SubscriptionDto(
    int Id,
    string Reference,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    decimal Price,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingAt,
    int? PricePointId,
    string? PricePointHandle,
    string? PricePointName)
{
    public static SubscriptionDto From(SubscriptionDetails subscription) => new(
        subscription.Id,
        subscription.Reference,
        subscription.PlanHandle,
        subscription.PlanName,
        subscription.PriceInCents,
        subscription.PriceInCents / 100m,
        subscription.Currency,
        subscription.State,
        subscription.NextBillingAt,
        subscription.PricePointId,
        subscription.PricePointHandle,
        subscription.PricePointName);
}

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> Plans);

public sealed record CreateSubscriptionResponse(SubscriptionDto Subscription);

public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);

public sealed class CreateSubscriptionRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}
