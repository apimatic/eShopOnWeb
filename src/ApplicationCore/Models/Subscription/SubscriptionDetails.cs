using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

/// <summary>
/// A recurring subscription of a buyer, as reflected by the billing system of record.
/// </summary>
public class SubscriptionDetails
{
    public SubscriptionDetails(string buyerId, string customerReference, int maxioCustomerId,
        int maxioSubscriptionId, string productHandle, string productName, int priceInCents,
        int interval, string intervalUnit, string state, string currency,
        DateTimeOffset? nextBillingDate, DateTimeOffset? activatedAt, DateTimeOffset? canceledAt,
        bool alreadyExisted)
    {
        BuyerId = buyerId;
        CustomerReference = customerReference;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
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
    public string CustomerReference { get; }
    public int MaxioCustomerId { get; }
    public int MaxioSubscriptionId { get; }
    public string ProductHandle { get; }
    public string ProductName { get; }
    public int PriceInCents { get; }
    public int Interval { get; }
    public string IntervalUnit { get; }
    public string State { get; }
    public string Currency { get; }
    public DateTimeOffset? NextBillingDate { get; }
    public DateTimeOffset? ActivatedAt { get; }
    public DateTimeOffset? CanceledAt { get; }
    /// <summary>True when the subscription already existed (idempotent repeat request).</summary>
    public bool AlreadyExisted { get; }

    public decimal Price => PriceInCents / 100m;
}
