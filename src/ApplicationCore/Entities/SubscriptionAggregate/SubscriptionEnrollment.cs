using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class SubscriptionEnrollment : BaseEntity, IAggregateRoot
{
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string userId, string productHandle, string reference, string attemptToken)
    {
        UserId = userId;
        ProductHandle = productHandle;
        Reference = reference;
        AttemptToken = attemptToken;
        Status = SubscriptionEnrollmentStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string Reference { get; private set; } = string.Empty;
    public string AttemptToken { get; private set; } = string.Empty;
    public SubscriptionEnrollmentStatus Status { get; private set; }
    public long? MaxioCustomerId { get; private set; }
    public long? MaxioSubscriptionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void TakeOwnership(string attemptToken)
    {
        AttemptToken = attemptToken;
        Status = SubscriptionEnrollmentStatus.Pending;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Complete(long customerId, long subscriptionId)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        Status = SubscriptionEnrollmentStatus.Complete;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Fail()
    {
        Status = SubscriptionEnrollmentStatus.Failed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public enum SubscriptionEnrollmentStatus
{
    Pending,
    Complete,
    Failed
}
