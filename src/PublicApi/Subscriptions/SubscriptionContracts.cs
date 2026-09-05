using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscribeRequest
{
    public string ProductHandle { get; init; } = string.Empty;
}

public sealed record SubscriptionPlanResponse(
    string Handle,
    string Name,
    decimal Price,
    int? Interval,
    string? IntervalUnit);

public sealed record SubscriptionResponse(
    string Reference,
    string PlanHandle,
    string PlanName,
    decimal Price,
    string? State,
    DateTimeOffset? NextBillingDate);
