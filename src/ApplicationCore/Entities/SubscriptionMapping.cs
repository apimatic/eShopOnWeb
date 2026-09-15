using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Durable correlation between an eShop user and the corresponding Maxio records.
/// Maxio remains the billing system of record; this entity is an integration index.
/// </summary>
public class SubscriptionMapping : BaseEntity
{
    private SubscriptionMapping()
    {
    }

    public SubscriptionMapping(
        string userId,
        string productHandle,
        string customerReference,
        int maxioCustomerId,
        int maxioSubscriptionId,
        string subscriptionReference)
    {
        UserId = userId;
        ProductHandle = productHandle;
        CustomerReference = customerReference;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        SubscriptionReference = subscriptionReference;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string CustomerReference { get; private set; } = string.Empty;
    public int MaxioCustomerId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public string SubscriptionReference { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void UpdateMaxioIds(int customerId, int subscriptionId)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
