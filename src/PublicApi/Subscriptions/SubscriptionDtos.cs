using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlanDto(string Handle, string Name, string? Description, long PriceInCents, int Interval, string IntervalUnit);
public sealed record SubscriptionDto(long Id, string PlanHandle, string PlanName, long PriceInCents, string State, DateTimeOffset? NextBillingAt);

public sealed class SubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; } = new();
}

public sealed class SubscriptionPlansRequest { }

public sealed class SubscribeRequest
{
    public string? PlanHandle { get; init; }
}

public sealed class SubscribeResponse
{
    public SubscribeResponse(SubscriptionDto subscription) => Subscription = subscription;
    public SubscriptionDto Subscription { get; }
}

public sealed class MySubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; } = new();
}

public sealed class MySubscriptionsRequest { }
