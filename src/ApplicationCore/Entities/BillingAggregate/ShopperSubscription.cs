using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

public class ShopperSubscription
{
    public long Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public decimal Price { get; init; }
    public DateTimeOffset? NextBillingAt { get; init; }
}
