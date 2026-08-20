using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public class SubscriptionRecord : BaseEntity, IAggregateRoot
{
    private SubscriptionRecord() { }

    public SubscriptionRecord(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        long maxioCustomerId,
        long maxioSubscriptionId)
    {
        UserId = userId;
        ProductHandle = productHandle;
        CustomerReference = customerReference;
        SubscriptionReference = subscriptionReference;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public string UserId { get; private set; } = null!;
    public string ProductHandle { get; private set; } = null!;
    public string CustomerReference { get; private set; } = null!;
    public string SubscriptionReference { get; private set; } = null!;
    public long MaxioCustomerId { get; private set; }
    public long MaxioSubscriptionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Reconcile(long maxioCustomerId, long maxioSubscriptionId)
    {
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
