using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class ShopperSubscription
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public decimal Price => PriceInCents / 100m;
    public DateTimeOffset? NextBillingDate { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? Reference { get; init; }
}
