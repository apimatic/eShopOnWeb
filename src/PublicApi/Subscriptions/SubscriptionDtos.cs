using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int BillingInterval { get; init; }
    public string BillingIntervalUnit { get; init; } = string.Empty;
}

public sealed class SubscriptionDto
{
    public long Id { get; init; }
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public int BillingInterval { get; init; }
    public string BillingIntervalUnit { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; init; }
}

public sealed class SubscribeRequest
{
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed class SubscribeResponse
{
    public bool Created { get; init; }
    public SubscriptionDto Subscription { get; init; } = new();
}

public sealed class MySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = Array.Empty<SubscriptionDto>();
}
