using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public class SubscriptionEnrollment : BaseEntity
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
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string CustomerReference { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public long? MaxioCustomerId { get; private set; }
    public long? MaxioSubscriptionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void RecordMaxioIds(long customerId, long subscriptionId, DateTimeOffset updatedAt)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        UpdatedAt = updatedAt;
    }
}
