using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlanDto(
    int? Id,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    decimal Price,
    int Interval,
    string IntervalUnit,
    bool CanSubscribeWithoutPayment);

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public sealed record SubscriptionDto(
    int Id,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    decimal Price,
    string State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CurrentPeriodEndsAt,
    string? Currency,
    string? Reference);
