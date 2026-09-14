using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class UserSubscription : BaseEntity, IAggregateRoot
{
    private UserSubscription()
    {
    }

    public UserSubscription(string userId, long maxioCustomerId, long maxioSubscriptionId,
        string reference, string productHandle, string productName, long priceInCents,
        int interval, string intervalUnit, string state, DateTimeOffset? nextBillingDate)
    {
        UserId = userId;
        Reference = reference;
        Synchronize(maxioCustomerId, maxioSubscriptionId, productHandle, productName,
            priceInCents, interval, intervalUnit, state, nextBillingDate);
    }

    public string UserId { get; private set; } = null!;
    public long MaxioCustomerId { get; private set; }
    public long MaxioSubscriptionId { get; private set; }
    public string Reference { get; private set; } = null!;
    public string ProductHandle { get; private set; } = null!;
    public string ProductName { get; private set; } = null!;
    public long PriceInCents { get; private set; }
    public int Interval { get; private set; }
    public string IntervalUnit { get; private set; } = null!;
    public string State { get; private set; } = null!;
    public DateTimeOffset? NextBillingDate { get; private set; }
    public DateTimeOffset LastSynchronizedAt { get; private set; }

    public void Synchronize(long maxioCustomerId, long maxioSubscriptionId,
        string productHandle, string productName, long priceInCents, int interval,
        string intervalUnit, string state, DateTimeOffset? nextBillingDate)
    {
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        State = state;
        NextBillingDate = nextBillingDate;
        LastSynchronizedAt = DateTimeOffset.UtcNow;
    }
}
