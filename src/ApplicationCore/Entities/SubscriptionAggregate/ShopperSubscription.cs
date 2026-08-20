using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class ShopperSubscription
{
    public ShopperSubscription(
        int id,
        string productHandle,
        string productName,
        decimal price,
        string state,
        DateTimeOffset? nextBillingDate)
    {
        Id = id;
        ProductHandle = productHandle;
        ProductName = productName;
        Price = price;
        State = state;
        NextBillingDate = nextBillingDate;
    }

    public int Id { get; }
    public string ProductHandle { get; }
    public string ProductName { get; }
    public decimal Price { get; }
    public string State { get; }
    public DateTimeOffset? NextBillingDate { get; }
}
