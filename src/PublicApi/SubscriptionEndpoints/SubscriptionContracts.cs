using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscribeRequest
{
    [Required]
    [StringLength(256)]
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    int? PriceInCents,
    int? Interval,
    string? IntervalUnit);

public sealed record SubscriptionDto(
    int? Id,
    string? PlanHandle,
    string? PlanName,
    int? PriceInCents,
    string? Currency,
    string? State,
    DateTimeOffset? NextBillingDate);

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> Plans);
public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);
