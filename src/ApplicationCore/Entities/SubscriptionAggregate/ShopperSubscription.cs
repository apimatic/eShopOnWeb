using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class ShopperSubscription
{
    public int Id { get; init; }
    public string? Reference { get; init; }
    public string? ProductHandle { get; init; }
    public string? ProductName { get; init; }
    public decimal Price { get; init; }
    public string? Currency { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
}
