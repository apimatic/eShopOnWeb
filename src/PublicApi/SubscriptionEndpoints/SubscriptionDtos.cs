using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanDto
{
    public string Name { get; init; } = string.Empty;
    public string Handle { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long? PriceInCents { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public string? PricePointHandle { get; init; }
}

public sealed class SubscriptionDto
{
    public int Id { get; init; }
    public string? Reference { get; init; }
    public string? ProductName { get; init; }
    public string ProductHandle { get; init; } = string.Empty;
    public long? PriceInCents { get; init; }
    public string? State { get; init; }
    public DateTimeOffset? NextBillingAt { get; init; }
}

public sealed class ListSubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; init; } = Array.Empty<SubscriptionPlanDto>();
}

public sealed class CreateSubscriptionRequest
{
    public string? ProductHandle { get; init; }
}

public sealed class CreateSubscriptionResponse
{
    public required SubscriptionDto Subscription { get; init; }
}

public sealed class ListMySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = Array.Empty<SubscriptionDto>();
}
