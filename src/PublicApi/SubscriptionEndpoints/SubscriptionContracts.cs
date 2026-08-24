using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanDto
{
    public string ProductHandle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long? PriceInCents { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public bool RequestsCreditCard { get; init; }
    public bool RequiresCreditCard { get; init; }
}

public sealed class ListSubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; init; } = Array.Empty<SubscriptionPlanDto>();
}

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; init; } = string.Empty;
}

public sealed class SubscriptionDto
{
    public int Id { get; init; }
    public string ProductHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long? PriceInCents { get; init; }
    public long? CurrentBillingAmountInCents { get; init; }
    public string? Currency { get; init; }
    public string? State { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
}

public sealed class CreateSubscriptionResponse
{
    public bool Created { get; init; }
    public required SubscriptionDto Subscription { get; init; }
}

public sealed class ListMySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = Array.Empty<SubscriptionDto>();
}
