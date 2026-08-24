using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class SubscriptionEnrollment : BaseEntity, IAggregateRoot
{
    public const string PendingStatus = "pending";
    public const string CompletedStatus = "completed";

    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(
        string userId,
        string productHandle,
        string subscriptionReference,
        DateTimeOffset leaseExpiresAt)
    {
        UserId = userId;
        ProductHandle = productHandle;
        SubscriptionReference = subscriptionReference;
        Status = PendingStatus;
        LeaseExpiresAt = leaseExpiresAt;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public string Status { get; private set; } = PendingStatus;
    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsCompleted => Status == CompletedStatus;

    public void AcquireLease(DateTimeOffset leaseExpiresAt)
    {
        Status = PendingStatus;
        LeaseExpiresAt = leaseExpiresAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ReleaseLease()
    {
        LeaseExpiresAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Complete(int customerId, int subscriptionId)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        Status = CompletedStatus;
        LeaseExpiresAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
