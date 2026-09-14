using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class SubscriptionLink : BaseEntity, IAggregateRoot
{
    private SubscriptionLink() { }

    public SubscriptionLink(
        string userId,
        string productHandle,
        long maxioCustomerId,
        long maxioSubscriptionId,
        string customerReference,
        string subscriptionReference,
        DateTimeOffset createdAt)
    {
        UserId = userId;
        ProductHandle = productHandle;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        CustomerReference = customerReference;
        SubscriptionReference = subscriptionReference;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public long MaxioCustomerId { get; private set; }
    public long MaxioSubscriptionId { get; private set; }
    public string CustomerReference { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Refresh(long maxioCustomerId, long maxioSubscriptionId, DateTimeOffset updatedAt)
    {
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        UpdatedAt = updatedAt;
    }
}
