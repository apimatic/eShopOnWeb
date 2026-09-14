using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Stores ownership and creation coordination only. Maxio remains the source of
/// truth for subscription pricing, state, and billing dates.
/// </summary>
public class SubscriptionRecord : BaseEntity, IAggregateRoot
{
    private SubscriptionRecord() { }

    public SubscriptionRecord(string userId, string productHandle, string subscriptionReference,
        string creationToken, DateTimeOffset leaseExpiresAt)
    {
        UserId = userId;
        ProductHandle = productHandle;
        SubscriptionReference = subscriptionReference;
        CreationToken = creationToken;
        CreationLeaseExpiresAt = leaseExpiresAt;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public string UserId { get; private set; } = null!;
    public string ProductHandle { get; private set; } = null!;
    public string SubscriptionReference { get; private set; } = null!;
    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public string CreationToken { get; private set; } = null!;
    public DateTimeOffset CreationLeaseExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Complete(int customerId, int subscriptionId)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        CreationLeaseExpiresAt = DateTimeOffset.MinValue;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RenewLease(string creationToken, DateTimeOffset leaseExpiresAt)
    {
        CreationToken = creationToken;
        CreationLeaseExpiresAt = leaseExpiresAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
