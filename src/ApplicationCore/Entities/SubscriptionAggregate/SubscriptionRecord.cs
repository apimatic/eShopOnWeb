using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Local record of a recurring subscription held by an eShopOnWeb buyer in the
/// Maxio Advanced Billing system of record. Keyed uniquely by (BuyerId, ProductHandle).
/// </summary>
public class SubscriptionRecord : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    public SubscriptionRecord() { }
#pragma warning restore CS8618

    public SubscriptionRecord(string buyerId, string customerReference, int maxioCustomerId,
        int maxioSubscriptionId, string productHandle, string productName,
        int priceInCents, int interval, string intervalUnit, string state, string currency,
        DateTimeOffset? nextBillingDate, DateTimeOffset? activatedAt)
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
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string CustomerReference { get; private set; }
    public int MaxioCustomerId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public string ProductHandle { get; private set; }
    public string ProductName { get; private set; }
    public int PriceInCents { get; private set; }
    public int Interval { get; private set; }
    public string IntervalUnit { get; private set; }
    public string State { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset? NextBillingDate { get; private set; }
    public DateTimeOffset? ActivatedAt { get; private set; }
    public DateTimeOffset? CanceledAt { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void RefreshFromBillingSystem(string state, string productName, int priceInCents,
        int interval, string intervalUnit, string currency,
        DateTimeOffset? nextBillingDate, DateTimeOffset? activatedAt, DateTimeOffset? canceledAt)
    {
        State = state;
        ProductName = productName;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        Currency = currency;
        NextBillingDate = nextBillingDate;
        ActivatedAt = activatedAt;
        CanceledAt = canceledAt;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
