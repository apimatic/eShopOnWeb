using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionDto
{
    public long Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string PricePointName { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; init; }

    public static SubscriptionDto From(SubscriptionDetails subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        PriceInCents = subscription.PriceInCents,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        PricePointName = subscription.PricePointName,
        NextBillingAt = subscription.NextBillingAt
    };
}
