using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Local correlation data only. Maxio remains the source of truth for subscription state.
/// </summary>
public class SubscriptionEnrollment : BaseEntity, IAggregateRoot
{
    private SubscriptionEnrollment()
    {
    }

    public SubscriptionEnrollment(
        string userId,
        string productHandle,
        int maxioCustomerId,
        int maxioSubscriptionId)
    {
        UserId = userId;
        ProductHandle = productHandle;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public int MaxioCustomerId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void UpdateMaxioIds(int customerId, int subscriptionId)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
