using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscribeRequest
{
    public string? PlanHandle { get; init; }
}

public sealed record SubscriptionPlanResponse(string Handle, string Name, int PriceInCents, int Interval, string IntervalUnit);

public sealed record SubscriptionResponse(
    int Id,
    string Reference,
    string PlanHandle,
    string PlanName,
    int PriceInCents,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingAt);

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanResponse> SubscriptionPlans);

public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionResponse> Subscriptions);

public sealed record SubscribeResponse(SubscriptionResponse? Subscription, bool IsPending, string Reference);
