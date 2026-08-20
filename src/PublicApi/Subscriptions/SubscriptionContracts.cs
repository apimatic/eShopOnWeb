using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string? PricePointName { get; init; }
    public bool RequiresPaymentMethod { get; init; }
}

public sealed class SubscriptionDto
{
    public long Id { get; init; }
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; init; }
}

public sealed class SubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; init; } = Array.Empty<SubscriptionPlanDto>();
}

public sealed class MySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = Array.Empty<SubscriptionDto>();
}

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; init; } = string.Empty;
}

public sealed class CreateSubscriptionResponse
{
    public SubscriptionDto Subscription { get; init; } = new();
    public bool AlreadySubscribed { get; init; }
}

public sealed class SubscribeResult
{
    public SubscriptionDto Subscription { get; init; } = new();
    public bool AlreadySubscribed { get; init; }
}
