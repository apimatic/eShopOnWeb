using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

public class SubscriptionEnrollment : BaseEntity
{
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string userId, string productHandle, string subscriptionReference)
    {
        UserId = userId;
        ProductHandle = productHandle;
        SubscriptionReference = subscriptionReference;
        Status = SubscriptionEnrollmentStatus.Processing;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public SubscriptionEnrollmentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public void Complete(int customerId, int subscriptionId)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        Status = SubscriptionEnrollmentStatus.Succeeded;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkReconciling(int? customerId)
    {
        MaxioCustomerId = customerId;
        Status = SubscriptionEnrollmentStatus.Reconciling;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(int? customerId)
    {
        MaxioCustomerId = customerId;
        Status = SubscriptionEnrollmentStatus.Failed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public enum SubscriptionEnrollmentStatus
{
    Processing,
    Reconciling,
    Succeeded,
    Failed
}
