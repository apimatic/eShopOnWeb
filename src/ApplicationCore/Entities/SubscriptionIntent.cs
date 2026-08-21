using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public sealed class SubscriptionIntent : BaseEntity, IAggregateRoot
{
    private SubscriptionIntent()
    {
        UserId = string.Empty;
        ProductHandle = string.Empty;
        SubscriptionReference = string.Empty;
    }

    public SubscriptionIntent(string userId, string productHandle, string subscriptionReference)
    {
        UserId = userId;
        ProductHandle = productHandle;
        SubscriptionReference = subscriptionReference;
        Status = SubscriptionIntentStatus.Processing;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public string UserId { get; private set; }
    public string ProductHandle { get; private set; }
    public string SubscriptionReference { get; private set; }
    public SubscriptionIntentStatus Status { get; private set; }
    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkActive(int? customerId, int subscriptionId)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        Status = SubscriptionIntentStatus.Active;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkProcessing()
    {
        Status = SubscriptionIntentStatus.Processing;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkOutcomeUnknown()
    {
        Status = SubscriptionIntentStatus.OutcomeUnknown;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkRejected()
    {
        Status = SubscriptionIntentStatus.Rejected;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public enum SubscriptionIntentStatus
{
    Processing,
    Active,
    OutcomeUnknown,
    Rejected
}
