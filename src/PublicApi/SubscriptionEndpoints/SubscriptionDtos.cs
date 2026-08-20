using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanDto
{
    public long ProductId { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
}

public sealed class SubscriptionDto
{
    public long Id { get; init; }
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; init; }
}

public sealed class ListSubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanDto> SubscriptionPlans { get; init; } = Array.Empty<SubscriptionPlanDto>();
}

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; init; } = string.Empty;
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}

public sealed class CreateSubscriptionResponse
{
    public SubscriptionDto Subscription { get; init; } = new();
    public bool Created { get; init; }
}

public sealed class ListMySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = Array.Empty<SubscriptionDto>();
}
