using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A local link between an eShopOnWeb user and a subscription
/// that exists in the external billing system of record (Maxio).
/// </summary>
public class SubscriptionLink : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public string SubscriptionReference { get; private set; }
    public string ProductHandle { get; private set; }
    public int MaxioCustomerId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private SubscriptionLink() { }

    public SubscriptionLink(string userId, string subscriptionReference, string productHandle,
        int maxioCustomerId, int maxioSubscriptionId)
    {
        UserId = userId;
        SubscriptionReference = subscriptionReference;
        ProductHandle = productHandle;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public void UpdateFromMaxio(int maxioCustomerId, int maxioSubscriptionId, string productHandle)
    {
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        ProductHandle = productHandle;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}