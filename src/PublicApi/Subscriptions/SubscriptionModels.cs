using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlanDto(
    int ProductId,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    decimal Price,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record SubscriptionDto(
    int SubscriptionId,
    int CustomerId,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    decimal Price,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt);

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> Plans);
public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);
