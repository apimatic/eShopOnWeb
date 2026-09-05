using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscribeRequest
{
    [Required]
    [StringLength(255)]
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    bool? RequireCreditCard);

public sealed record SubscriptionDto(
    int? Id,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? State,
    DateTimeOffset? NextBillingDate);

public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);
