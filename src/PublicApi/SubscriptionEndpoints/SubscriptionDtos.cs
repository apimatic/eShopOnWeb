using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }
}

public sealed class SubscriptionDto
{
    public long Id { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
}

public sealed class SubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; set; } = Array.Empty<SubscriptionPlanDto>();
}

public sealed class MySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; set; } = Array.Empty<SubscriptionDto>();
}

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public sealed class CreateSubscriptionResponse
{
    public bool Created { get; set; }
    public SubscriptionDto Subscription { get; set; } = new();
}
