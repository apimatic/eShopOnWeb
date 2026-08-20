using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class ShopperSubscription
{
    public int Id { get; init; }
    public required string ProductHandle { get; init; }
    public required string ProductName { get; init; }
    public decimal Price { get; init; }
    public int Interval { get; init; }
    public required string IntervalUnit { get; init; }
    public required string State { get; init; }
    public DateTimeOffset? NextBillingAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}
