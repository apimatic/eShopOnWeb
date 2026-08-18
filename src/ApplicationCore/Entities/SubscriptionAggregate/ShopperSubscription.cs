using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class ShopperSubscription
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string? ProductHandle { get; init; }
    public string? ProductName { get; init; }
    public long PriceInCents { get; init; }
    public decimal Price { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public string? Reference { get; init; }
    public int? CustomerId { get; init; }
}
