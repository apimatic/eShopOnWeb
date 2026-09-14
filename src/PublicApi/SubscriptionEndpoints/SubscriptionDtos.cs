using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record SubscriptionDto(
    int Id,
    string? Reference,
    string ProductHandle,
    string ProductName,
    long? ProductPriceInCents,
    long? CurrentBillingAmountInCents,
    string State,
    DateTimeOffset? NextBillingDate);

public sealed record SubscribeResult(SubscriptionDto Subscription, bool Created);

public sealed class SubscribeRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}
