using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Local reconciliation snapshot. Maxio Advanced Billing remains the system of record.
/// </summary>
public class SubscriptionRecord : BaseEntity
{
    private SubscriptionRecord()
    {
    }

    public SubscriptionRecord(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        string subscriptionUniquenessToken,
        string productName,
        long priceInCents,
        int interval,
        string intervalUnit)
    {
        UserId = userId;
        ProductHandle = productHandle;
        CustomerReference = customerReference;
        SubscriptionReference = subscriptionReference;
        SubscriptionUniquenessToken = subscriptionUniquenessToken;
        ProductName = productName;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        State = "pending";
    }

    public string UserId { get; private set; } = null!;
    public string ProductHandle { get; private set; } = null!;
    public string CustomerReference { get; private set; } = null!;
    public string SubscriptionReference { get; private set; } = null!;
    public string SubscriptionUniquenessToken { get; private set; } = null!;
    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public string ProductName { get; private set; } = null!;
    public long PriceInCents { get; private set; }
    public string? Currency { get; private set; }
    public int Interval { get; private set; }
    public string IntervalUnit { get; private set; } = null!;
    public string State { get; private set; } = null!;
    public DateTimeOffset? NextBillingAt { get; private set; }
    public DateTimeOffset? SynchronizedAt { get; private set; }

    public void RotateSubscriptionUniquenessToken(string token)
    {
        SubscriptionUniquenessToken = token;
    }

    public void Synchronize(
        string productHandle,
        string productName,
        long priceInCents,
        string? currency,
        int interval,
        string intervalUnit,
        string state,
        DateTimeOffset? nextBillingAt,
        int maxioCustomerId,
        int maxioSubscriptionId,
        DateTimeOffset synchronizedAt)
    {
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        Currency = currency;
        Interval = interval;
        IntervalUnit = intervalUnit;
        State = state;
        NextBillingAt = nextBillingAt;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        SynchronizedAt = synchronizedAt;
    }
}
