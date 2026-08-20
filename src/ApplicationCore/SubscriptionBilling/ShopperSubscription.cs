using System;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

public sealed class ShopperSubscription
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public DateTimeOffset? NextBillingAt { get; init; }
}
