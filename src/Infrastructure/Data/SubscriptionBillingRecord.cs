using System;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// A local idempotency reservation and Maxio identifier mapping. Maxio remains the
/// system of record for subscription state and pricing.
/// </summary>
public class SubscriptionBillingRecord
{
    private SubscriptionBillingRecord() { }

    public SubscriptionBillingRecord(
        string userId,
        string productHandle,
        string subscriptionReference,
        Guid leaseToken,
        DateTimeOffset leaseExpiresAt)
    {
        UserId = userId;
        ProductHandle = productHandle;
        SubscriptionReference = subscriptionReference;
        Status = SubscriptionBillingStatus.Pending;
        LeaseToken = leaseToken;
        LeaseExpiresAt = leaseExpiresAt;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public SubscriptionBillingStatus Status { get; private set; }
    public Guid LeaseToken { get; private set; }
    public DateTimeOffset LeaseExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool HasActiveLease(DateTimeOffset now) =>
        Status == SubscriptionBillingStatus.Pending && LeaseExpiresAt > now;

    public void Claim(Guid leaseToken, DateTimeOffset leaseExpiresAt)
    {
        Status = SubscriptionBillingStatus.Pending;
        LeaseToken = leaseToken;
        LeaseExpiresAt = leaseExpiresAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetCustomer(int customerId)
    {
        MaxioCustomerId = customerId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Complete(int customerId, int subscriptionId)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        Status = SubscriptionBillingStatus.Completed;
        LeaseExpiresAt = DateTimeOffset.MinValue;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Fail()
    {
        Status = SubscriptionBillingStatus.Failed;
        LeaseExpiresAt = DateTimeOffset.MinValue;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public enum SubscriptionBillingStatus
{
    Pending,
    Completed,
    Failed
}
