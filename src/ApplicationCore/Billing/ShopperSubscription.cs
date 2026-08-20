using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class ShopperSubscription
{
    public int Id { get; init; }
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
}
