using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum EnrollmentStatus
{
    Pending,
    Succeeded,
    Unknown,
    Rejected
}

public class SubscriptionEnrollment
{
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string userId, string productHandle, string subscriptionReference)
    {
        UserId = userId;
        ProductHandle = productHandle;
        SubscriptionReference = subscriptionReference;
        Status = EnrollmentStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int Id { get; private set; }
    public string UserId { get; private set; } = null!;
    public string ProductHandle { get; private set; } = null!;
    public string SubscriptionReference { get; private set; } = null!;
    public int? MaxioSubscriptionId { get; private set; }
    public EnrollmentStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public byte[] Version { get; private set; } = Array.Empty<byte>();

    public void MarkSucceeded(int maxioSubscriptionId)
    {
        MaxioSubscriptionId = maxioSubscriptionId;
        Status = EnrollmentStatus.Succeeded;
        RejectionReason = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkUnknown()
    {
        Status = EnrollmentStatus.Unknown;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkRejected(string reason)
    {
        Status = EnrollmentStatus.Rejected;
        RejectionReason = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
