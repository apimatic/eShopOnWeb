using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public class SubscriptionProvisioningRecord : IAggregateRoot
{
    private SubscriptionProvisioningRecord() { }

    public SubscriptionProvisioningRecord(
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
        Status = SubscriptionProvisioningStatus.Pending;
        UpdatedAt = now;
        ConcurrencyToken = Guid.NewGuid();
    }

    public int Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string CustomerReference { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public long? MaxioCustomerId { get; private set; }
    public long? MaxioSubscriptionId { get; private set; }
    public SubscriptionProvisioningStatus Status { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? LastErrorCode { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    public void BeginAttempt(DateTimeOffset now)
    {
        Status = SubscriptionProvisioningStatus.Pending;
        UpdatedAt = now;
        LastErrorCode = null;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void Complete(long customerId, long subscriptionId, DateTimeOffset now)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        Status = SubscriptionProvisioningStatus.Succeeded;
        UpdatedAt = now;
        LastErrorCode = null;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void Fail(string errorCode, DateTimeOffset now)
    {
        Status = SubscriptionProvisioningStatus.Failed;
        UpdatedAt = now;
        LastErrorCode = errorCode;
        ConcurrencyToken = Guid.NewGuid();
    }
}

public enum SubscriptionProvisioningStatus
{
    Pending,
    Succeeded,
    Failed
}
