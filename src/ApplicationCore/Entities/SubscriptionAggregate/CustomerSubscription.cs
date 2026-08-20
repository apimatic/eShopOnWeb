using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class CustomerSubscription
{
    public long Id { get; }
    public string State { get; }
    public string ProductHandle { get; }
    public string ProductName { get; }
    public decimal Price { get; }
    public DateTimeOffset? NextBillingAt { get; }

    public CustomerSubscription(
        long id,
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

    public bool IsOpen => State is
        "active" or
        "trialing" or
        "past_due" or
        "unpaid" or
        "paused" or
        "on_hold" or
        "assessing" or
        "pending" or
        "soft_failure";
}
