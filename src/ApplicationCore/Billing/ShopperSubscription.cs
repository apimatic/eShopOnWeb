using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class ShopperSubscription
{
    public int Id { get; init; }
    public required string State { get; init; }
    public required string ProductHandle { get; init; }
    public required string ProductName { get; init; }
    public long PriceInCents { get; init; }
    public decimal Price => PriceInCents / 100m;
    public DateTimeOffset? NextBillingDate { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? ActivatedAt { get; init; }
    public string? Reference { get; init; }
}
