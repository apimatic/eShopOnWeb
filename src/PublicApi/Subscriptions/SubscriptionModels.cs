using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanDto
{
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public required string Currency { get; init; }
    public int Interval { get; init; }
    public required string IntervalUnit { get; init; }
    public bool RequiresPaymentMethod { get; init; }
    public bool CanSubscribe { get; init; }
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

public sealed class CreateSubscriptionResponse
{
    public required SubscriptionDto Subscription { get; init; }
    public bool AlreadyExisted { get; init; }
}

public sealed class ListMySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = Array.Empty<SubscriptionDto>();
}

public sealed class SubscriptionDto
{
    public int Id { get; init; }
    public required string State { get; init; }
    public required string ProductHandle { get; init; }
    public required string ProductName { get; init; }
    public long PriceInCents { get; init; }
    public required string Currency { get; init; }
    public int Interval { get; init; }
    public required string IntervalUnit { get; init; }
    public DateTimeOffset? NextBillingAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
