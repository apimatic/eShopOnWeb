using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class SubscriptionBillingRecord : BaseEntity, IAggregateRoot
{
    private SubscriptionBillingRecord() { }

    public SubscriptionBillingRecord(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        DateTimeOffset now)
    {
        UserId = userId;
        ProductHandle = productHandle;
        CustomerReference = customerReference;
        SubscriptionReference = subscriptionReference;
        Status = SubscriptionBillingRecordStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string UserId { get; private set; } = null!;
    public string ProductHandle { get; private set; } = null!;
    public string CustomerReference { get; private set; } = null!;
    public string SubscriptionReference { get; private set; } = null!;
    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public SubscriptionBillingRecordStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public byte[] Version { get; private set; } = [];

    public void MarkAttempt(DateTimeOffset now)
    {
        Status = SubscriptionBillingRecordStatus.Pending;
        UpdatedAt = now;
    }

    public void MarkCompleted(int customerId, int subscriptionId, DateTimeOffset now)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        Status = SubscriptionBillingRecordStatus.Completed;
        UpdatedAt = now;
    }

    public void MarkFailed(DateTimeOffset now)
    {
        Status = SubscriptionBillingRecordStatus.Failed;
        UpdatedAt = now;
    }
}

public enum SubscriptionBillingRecordStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2
}
