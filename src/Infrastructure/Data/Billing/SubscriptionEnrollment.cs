using System;

namespace Microsoft.eShopWeb.Infrastructure.Data.Billing;

public enum SubscriptionEnrollmentStatus
{
    Pending,
    Completed,
    Failed,
    NeedsReconciliation
}

public sealed class SubscriptionEnrollment
{
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string userKey, string productHandle, string subscriptionReference)
    {
        UserKey = userKey;
        ProductHandle = productHandle;
        SubscriptionReference = subscriptionReference;
        Status = SubscriptionEnrollmentStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int Id { get; private set; }
    public string UserKey { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public SubscriptionEnrollmentStatus Status { get; private set; }
    public int? RemoteSubscriptionId { get; private set; }
    public string? FailureCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public void Complete(int remoteSubscriptionId)
    {
        RemoteSubscriptionId = remoteSubscriptionId;
        FailureCode = null;
        Status = SubscriptionEnrollmentStatus.Completed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkNeedsReconciliation()
    {
        Status = SubscriptionEnrollmentStatus.NeedsReconciliation;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string failureCode)
    {
        FailureCode = failureCode;
        Status = SubscriptionEnrollmentStatus.Failed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Retry()
    {
        FailureCode = null;
        Status = SubscriptionEnrollmentStatus.Pending;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
