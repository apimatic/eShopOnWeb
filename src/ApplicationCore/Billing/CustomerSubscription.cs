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
        DateTimeOffset? nextBillingDate,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset createdAt)
    {
        Id = id;
        State = state;
        ProductHandle = productHandle;
        ProductName = productName;
        Price = price;
        NextBillingDate = nextBillingDate;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        CreatedAt = createdAt;
    }

    public int Id { get; }
    public string State { get; }
    public string ProductHandle { get; }
    public string ProductName { get; }
    public decimal Price { get; }
    public DateTimeOffset? NextBillingDate { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }
    public DateTimeOffset CreatedAt { get; }
}
