using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlanResponse(
    string Handle,
    string Name,
    string? Description,
    int PriceInCents,
    int Interval,
    string IntervalUnit,
    string? Currency);

public sealed record SubscriptionPlansResponse(IReadOnlyCollection<SubscriptionPlanResponse> Plans);

public sealed class CreateSubscriptionRequest
{
    [Required]
    [StringLength(255)]
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed record SubscriptionResponse(
    long Id,
    string PlanHandle,
    string PlanName,
    int PriceInCents,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingAt);

public sealed record MySubscriptionsResponse(IReadOnlyCollection<SubscriptionResponse> Subscriptions);
