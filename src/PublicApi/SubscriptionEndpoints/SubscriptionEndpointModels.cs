using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit)
{
    public static SubscriptionPlanDto From(SubscriptionPlan plan) =>
        new(plan.Handle, plan.Name, plan.Description, plan.PriceInCents, plan.Interval, plan.IntervalUnit);
}

public sealed class SubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; init; } = Array.Empty<SubscriptionPlanDto>();
}

public sealed class CreateSubscriptionRequest
{
    [Required]
    public string ProductHandle { get; init; } = string.Empty;
}

public sealed record SubscriptionDto(
    string Reference,
    string PlanHandle,
    string PlanName,
    long? PriceInCents,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingAt,
    bool IsPending)
{
    public static SubscriptionDto From(SubscriptionDetails details) =>
        new(
            details.Reference,
            details.PlanHandle,
            details.PlanName,
            details.PriceInCents,
            details.Currency,
            details.State,
            details.NextBillingAt,
            details.IsPending);
}

public sealed class MySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = Array.Empty<SubscriptionDto>();
}
