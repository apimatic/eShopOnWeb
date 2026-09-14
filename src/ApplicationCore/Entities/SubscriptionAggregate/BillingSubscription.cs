using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class BillingSubscription : BaseEntity, IAggregateRoot
{
    private BillingSubscription()
    {
    }

    public BillingSubscription(string userId, string productHandle, string subscriptionReference)
    {
        UserId = userId;
        ProductHandle = productHandle;
        SubscriptionReference = subscriptionReference;
        Status = BillingSubscriptionStatus.Pending;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public string UserId { get; private set; } = null!;
    public string ProductHandle { get; private set; } = null!;
    public string SubscriptionReference { get; private set; } = null!;
    public long? MaxioCustomerId { get; private set; }
    public long? MaxioSubscriptionId { get; private set; }
    public BillingSubscriptionStatus Status { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkPending()
    {
        Status = BillingSubscriptionStatus.Pending;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCompleted(long maxioCustomerId, long maxioSubscriptionId)
    {
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        Status = BillingSubscriptionStatus.Completed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed()
    {
        Status = BillingSubscriptionStatus.Failed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public enum BillingSubscriptionStatus
{
    Pending,
    Completed,
    Failed
}

