using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class ShopSubscription
{
    public int Id { get; init; }
    public string? ProductHandle { get; init; }
    public string? ProductName { get; init; }
    public decimal? Price { get; init; }
    public string? State { get; init; }
    public DateTimeOffset? NextBillingAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
}
