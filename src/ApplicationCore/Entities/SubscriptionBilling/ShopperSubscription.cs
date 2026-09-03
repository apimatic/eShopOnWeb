using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

public sealed record ShopperSubscription
{
    public int Id { get; init; }
    public required string ProductHandle { get; init; }
    public required string ProductName { get; init; }
    public decimal Price { get; init; }
    public required string State { get; init; }
    public DateTimeOffset? NextBillingAt { get; init; }
    public bool AlreadyExisted { get; init; }
}
