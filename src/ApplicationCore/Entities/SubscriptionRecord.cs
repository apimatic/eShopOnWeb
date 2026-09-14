using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A local index of a Maxio subscription. Maxio remains the source of truth.
/// </summary>
public class SubscriptionRecord : BaseEntity, IAggregateRoot
{
    private SubscriptionRecord() { }

    public SubscriptionRecord(
        string userId,
        long maxioCustomerId,
        string maxioCustomerReference,
        long maxioSubscriptionId,
        string subscriptionReference,
        string productHandle,
        string state,
        DateTimeOffset? nextBillingAt)
    {
        UserId = userId;
        ProductHandle = productHandle;
        Synchronize(
            maxioCustomerId,
            maxioCustomerReference,
            maxioSubscriptionId,
            subscriptionReference,
            state,
            nextBillingAt);
    }

    public string UserId { get; private set; } = string.Empty;
    public long MaxioCustomerId { get; private set; }
    public string MaxioCustomerReference { get; private set; } = string.Empty;
    public long MaxioSubscriptionId { get; private set; }
    public string SubscriptionReference { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; private set; }
    public DateTimeOffset SynchronizedAt { get; private set; }

    public void Synchronize(
        long maxioCustomerId,
        string maxioCustomerReference,
        long maxioSubscriptionId,
        string subscriptionReference,
        string state,
        DateTimeOffset? nextBillingAt)
    {
        MaxioCustomerId = maxioCustomerId;
        MaxioCustomerReference = maxioCustomerReference;
        MaxioSubscriptionId = maxioSubscriptionId;
        SubscriptionReference = subscriptionReference;
        State = state;
        NextBillingAt = nextBillingAt;
        SynchronizedAt = DateTimeOffset.UtcNow;
    }
}
