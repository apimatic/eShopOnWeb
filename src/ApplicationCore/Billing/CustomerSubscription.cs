using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class CustomerSubscription
{
    public CustomerSubscription(
        int id,
        string state,
        string productHandle,
        string productName,
        decimal price,
        DateTimeOffset? nextBillingAt)
    {
        Id = id;
        State = state;
        ProductHandle = productHandle;
        ProductName = productName;
        Price = price;
        NextBillingAt = nextBillingAt;
    }

    public int Id { get; }
    public string State { get; }
    public string ProductHandle { get; }
    public string ProductName { get; }
    public decimal Price { get; }
    public DateTimeOffset? NextBillingAt { get; }
}
