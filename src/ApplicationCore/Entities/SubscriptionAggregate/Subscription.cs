using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class Subscription : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private Subscription() { }

    public Subscription(string userId, int maxioCustomerId, int maxioSubscriptionId, int productId, string productHandle)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NegativeOrZero(maxioCustomerId, nameof(maxioCustomerId));
        Guard.Against.NegativeOrZero(maxioSubscriptionId, nameof(maxioSubscriptionId));
        Guard.Against.NegativeOrZero(productId, nameof(productId));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));

        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        ProductId = productId;
        ProductHandle = productHandle;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string UserId { get; private set; }
    public int MaxioCustomerId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public int ProductId { get; private set; }
    public string ProductHandle { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public void UpdateMaxioReferences(int customerId, int subscriptionId)
    {
        Guard.Against.NegativeOrZero(customerId, nameof(customerId));
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));

        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
