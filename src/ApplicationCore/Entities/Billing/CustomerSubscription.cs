using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

public sealed class CustomerSubscription
{
    public CustomerSubscription(
        long id,
        string productHandle,
        string productName,
        int priceInCents,
        string state,
        DateTimeOffset? nextBillingAt,
        DateTimeOffset? currentPeriodEndsAt)
    {
        Id = id;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        State = state;
        NextBillingAt = nextBillingAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
    }

    public long Id { get; }
    public string ProductHandle { get; }
    public string ProductName { get; }
    public int PriceInCents { get; }
    public decimal Price => PriceInCents / 100m;
    public string State { get; }
    public DateTimeOffset? NextBillingAt { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }
}
