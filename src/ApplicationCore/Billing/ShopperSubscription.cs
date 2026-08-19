using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class ShopperSubscription
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public decimal Price => PriceInCents / 100m;
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; init; }
    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public bool AlreadyExisted { get; init; }
}
