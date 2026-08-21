using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string PlanHandle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record SubscriptionDto(
    int Id,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string Currency,
    string State,
    DateTimeOffset? NextBillingAt);

public sealed class SubscribeRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}
