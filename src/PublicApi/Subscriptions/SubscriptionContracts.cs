using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public decimal Price => PriceInCents / 100m;
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
}

public sealed class ListSubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; init; } = Array.Empty<SubscriptionPlanDto>();
}

public sealed class CreateSubscriptionRequest
{
    [Required]
    public string ProductHandle { get; init; } = string.Empty;
}

public sealed class SubscriptionDto
{
    public long SubscriptionId { get; init; }
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public decimal Price => PriceInCents / 100m;
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string? Currency { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; init; }
}

public sealed class CreateSubscriptionResponse
{
    public SubscriptionDto Subscription { get; init; } = new();
}

public sealed class ListMySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = Array.Empty<SubscriptionDto>();
}
