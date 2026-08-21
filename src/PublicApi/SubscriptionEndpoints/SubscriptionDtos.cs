using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanDto
{
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public required string IntervalUnit { get; init; }
    public bool RequiresPaymentMethod { get; init; }
}

public sealed class SubscriptionDto
{
    public long Id { get; init; }
    public required string Reference { get; init; }
    public required string PlanHandle { get; init; }
    public required string PlanName { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public required string IntervalUnit { get; init; }
    public required string State { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
}

public sealed class SubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; init; } = [];
}

public sealed class MySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = [];
}

public sealed class SubscribeRequest
{
    [Required]
    public string ProductHandle { get; init; } = string.Empty;
}
