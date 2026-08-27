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
        string subscriptionReference)
    {
        UserId = userId;
        ProductHandle = productHandle;
        CustomerReference = customerReference;
        SubscriptionReference = subscriptionReference;
        Status = SubscriptionBillingStatus.Pending;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string CustomerReference { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public int? MaxioSubscriptionId { get; private set; }
    public string Status { get; private set; } = SubscriptionBillingStatus.Pending;
    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkCompleted(int maxioSubscriptionId)
    {
        MaxioSubscriptionId = maxioSubscriptionId;
        Status = SubscriptionBillingStatus.Completed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkReconciliationRequired()
    {
        Status = SubscriptionBillingStatus.ReconciliationRequired;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkPending()
    {
        Status = SubscriptionBillingStatus.Pending;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public static class SubscriptionBillingStatus
{
    public const string Pending = "pending";
    public const string Completed = "completed";
    public const string ReconciliationRequired = "reconciliation_required";
}
