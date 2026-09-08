using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A buyer's recurring subscription as held in the Maxio Advanced Billing
/// system of record.
/// </summary>
public class SubscriptionDto
{
    public SubscriptionDto(string buyerId, int maxioSubscriptionId, string productHandle, string productName,
        int priceInCents, decimal price, int interval, string intervalUnit, string state, string currency,
        DateTimeOffset? nextBillingDate, DateTimeOffset? activatedAt, DateTimeOffset? canceledAt, bool alreadyExisted)
    {
        BuyerId = buyerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        Price = price;
        Interval = interval;
        IntervalUnit = intervalUnit;
        State = state;
        Currency = currency;
        NextBillingDate = nextBillingDate;
        ActivatedAt = activatedAt;
        CanceledAt = canceledAt;
        AlreadyExisted = alreadyExisted;
    }

    public string BuyerId { get; }
    public int MaxioSubscriptionId { get; }
    public string ProductHandle { get; }
    public string ProductName { get; }
    public int PriceInCents { get; }
    public decimal Price { get; }
    public int Interval { get; }
    public string IntervalUnit { get; }
    public string State { get; }
    public string Currency { get; }
    public DateTimeOffset? NextBillingDate { get; }
    public DateTimeOffset? ActivatedAt { get; }
    public DateTimeOffset? CanceledAt { get; }
    public bool AlreadyExisted { get; }
}
