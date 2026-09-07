using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class Subscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public int MaxioCustomerId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public string ProductHandle { get; private set; }
    public string State { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? NextBillingAt { get; private set; }

    public Subscription(
        string userId,
        int maxioCustomerId,
        int maxioSubscriptionId,
        string productHandle,
        string state,
        DateTime createdAt,
        DateTime updatedAt,
        DateTime? nextBillingAt)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NegativeOrZero(maxioCustomerId, nameof(maxioCustomerId));
        Guard.Against.NegativeOrZero(maxioSubscriptionId, nameof(maxioSubscriptionId));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));
        Guard.Against.NullOrEmpty(state, nameof(state));

        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        ProductHandle = productHandle;
        State = state;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        NextBillingAt = nextBillingAt;
    }
}
