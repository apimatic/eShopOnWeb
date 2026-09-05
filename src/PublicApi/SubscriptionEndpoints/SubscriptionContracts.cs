using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public string Currency { get; init; } = string.Empty;
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
}

public sealed class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; } = new();
}

public sealed class CreateSubscriptionRequest
{
    [Required]
    [StringLength(255)]
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed class SubscriptionDto
{
    public long Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public string Currency { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; init; }
}

public sealed class CreateSubscriptionResponse
{
    public SubscriptionDto Subscription { get; init; } = new();
    public bool AlreadySubscribed { get; init; }
}

public sealed class ListMySubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; } = new();
}
