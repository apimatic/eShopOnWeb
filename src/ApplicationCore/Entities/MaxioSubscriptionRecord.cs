using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A local reconciliation record. Maxio remains the system of record for subscription state.
/// </summary>
public class MaxioSubscriptionRecord : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public string ProductHandle { get; private set; }
    public string CustomerReference { get; private set; }
    public long MaxioCustomerId { get; private set; }
    public string SubscriptionReference { get; private set; }
    public long MaxioSubscriptionId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private MaxioSubscriptionRecord()
    {
        UserId = null!;
        ProductHandle = null!;
        CustomerReference = null!;
        SubscriptionReference = null!;
    }

    public MaxioSubscriptionRecord(
        string userId,
        string productHandle,
        string customerReference,
        long maxioCustomerId,
        string subscriptionReference,
        long maxioSubscriptionId)
    {
        UserId = Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        ProductHandle = Guard.Against.NullOrWhiteSpace(productHandle, nameof(productHandle));
        CustomerReference = Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        SubscriptionReference = Guard.Against.NullOrWhiteSpace(subscriptionReference, nameof(subscriptionReference));
        UpdateMaxioIds(maxioCustomerId, maxioSubscriptionId);
    }

    public void UpdateMaxioIds(long maxioCustomerId, long maxioSubscriptionId)
    {
        MaxioCustomerId = Guard.Against.NegativeOrZero(maxioCustomerId, nameof(maxioCustomerId));
        MaxioSubscriptionId = Guard.Against.NegativeOrZero(maxioSubscriptionId, nameof(maxioSubscriptionId));
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
