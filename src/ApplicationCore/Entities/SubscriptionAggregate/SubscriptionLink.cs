using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Locally persisted correlation data only. Maxio is the system of record for billing state.
/// </summary>
public sealed class SubscriptionLink : BaseEntity, IAggregateRoot
{
    private SubscriptionLink() { }

    public SubscriptionLink(string userId, string productHandle, string subscriptionReference)
    {
        UserId = userId;
        ProductHandle = productHandle;
        SubscriptionReference = subscriptionReference;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public long? MaxioCustomerId { get; private set; }
    public long? MaxioSubscriptionId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Synchronize(long customerId, long subscriptionId)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
