using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
}

public sealed class SubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; } = new();
}

public sealed class CreateSubscriptionRequest
{
    [Required]
    [StringLength(255)]
    public string ProductHandle { get; init; } = string.Empty;
}

public sealed class SubscriptionDto
{
    public long Id { get; init; }
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; init; }
}

public sealed class MySubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; } = new();
}
