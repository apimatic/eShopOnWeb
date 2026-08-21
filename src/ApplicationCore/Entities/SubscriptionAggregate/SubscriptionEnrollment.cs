using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class SubscriptionEnrollment : BaseEntity, IAggregateRoot
{
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt)
    {
        UserId = userId;
        ProductHandle = productHandle;
        CustomerReference = customerReference;
        SubscriptionReference = subscriptionReference;
        Status = SubscriptionEnrollmentStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
        LeaseExpiresAt = leaseExpiresAt;
        Version = Guid.NewGuid();
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string CustomerReference { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public SubscriptionEnrollmentStatus Status { get; private set; }
    public long? MaxioCustomerId { get; private set; }
    public long? MaxioSubscriptionId { get; private set; }
    public DateTimeOffset LeaseExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? LastError { get; private set; }
    public Guid Version { get; private set; }

    public void BeginAttempt(DateTimeOffset now, DateTimeOffset leaseExpiresAt)
    {
        Status = SubscriptionEnrollmentStatus.Pending;
        LeaseExpiresAt = leaseExpiresAt;
        UpdatedAt = now;
        LastError = null;
        Version = Guid.NewGuid();
    }

    public void Complete(long maxioCustomerId, long maxioSubscriptionId, DateTimeOffset now)
    {
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        Status = SubscriptionEnrollmentStatus.Active;
        LeaseExpiresAt = now;
        UpdatedAt = now;
        LastError = null;
        Version = Guid.NewGuid();
    }

    public void Fail(string error, DateTimeOffset now)
    {
        Status = SubscriptionEnrollmentStatus.Failed;
        LeaseExpiresAt = now;
        UpdatedAt = now;
        LastError = error;
        Version = Guid.NewGuid();
    }
}

public enum SubscriptionEnrollmentStatus
{
    Pending,
    Active,
    Failed
}
