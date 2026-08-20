using System;
using Microsoft.eShopWeb.PublicApi.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionDto
{
    public long Id { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string ProductHandle { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; init; }

    public static SubscriptionDto From(SubscriptionView subscription) => new()
    {
        Id = subscription.Id,
        ProductName = subscription.ProductName,
        ProductHandle = subscription.ProductHandle,
        PriceInCents = subscription.PriceInCents,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        State = subscription.State,
        NextBillingAt = subscription.NextBillingAt
    };
}
