using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class SubscriptionRecord : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public string ProductHandle { get; private set; }
    public string SubscriptionReference { get; private set; }
    public long MaxioCustomerId { get; private set; }
    public long MaxioSubscriptionId { get; private set; }
    public DateTimeOffset SynchronizedAtUtc { get; private set; }

    private SubscriptionRecord()
    {
        UserId = string.Empty;
        ProductHandle = string.Empty;
        SubscriptionReference = string.Empty;
    }

    public SubscriptionRecord(
        string userId,
        string productHandle,
        string subscriptionReference,
        long maxioCustomerId,
        long maxioSubscriptionId,
        DateTimeOffset synchronizedAtUtc)
    {
        UserId = userId;
        ProductHandle = productHandle;
        SubscriptionReference = subscriptionReference;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        SynchronizedAtUtc = synchronizedAtUtc;
    }

    public void Synchronize(long customerId, long subscriptionId, DateTimeOffset synchronizedAtUtc)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        SynchronizedAtUtc = synchronizedAtUtc;
    }
}
