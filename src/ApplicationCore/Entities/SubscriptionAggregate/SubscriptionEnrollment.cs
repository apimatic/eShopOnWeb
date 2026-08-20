using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class SubscriptionEnrollment : BaseEntity, IAggregateRoot
{
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        DateTimeOffset createdAt)
    {
        UserId = userId;
        ProductHandle = productHandle;
        CustomerReference = customerReference;
        SubscriptionReference = subscriptionReference;
        Status = SubscriptionEnrollmentStatus.Pending;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string CustomerReference { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public SubscriptionEnrollmentStatus Status { get; private set; }
    public DateTimeOffset? SendAuthorizedAt { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public void AuthorizeSingleSend(DateTimeOffset now)
    {
        if (SendAuthorizedAt.HasValue)
        {
            throw new InvalidOperationException("A subscription create was already authorized.");
        }

        SendAuthorizedAt = now;
        Status = SubscriptionEnrollmentStatus.SendAuthorized;
        UpdatedAt = now;
    }

    public void Confirm(int maxioSubscriptionId, DateTimeOffset now)
    {
        MaxioSubscriptionId = maxioSubscriptionId;
        Status = SubscriptionEnrollmentStatus.Confirmed;
        UpdatedAt = now;
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = SubscriptionEnrollmentStatus.Unknown;
        UpdatedAt = now;
    }

    public void MarkRejected(DateTimeOffset now)
    {
        Status = SubscriptionEnrollmentStatus.Rejected;
        UpdatedAt = now;
    }
}

public enum SubscriptionEnrollmentStatus
{
    Pending,
    SendAuthorized,
    Confirmed,
    Unknown,
    Rejected
}
