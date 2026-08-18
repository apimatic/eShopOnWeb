using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class ShopperSubscription
{
    public int? Id { get; init; }
    public string? ProductHandle { get; init; }
    public string? ProductName { get; init; }
    public long? PriceInCents { get; init; }
    public string? State { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
}
