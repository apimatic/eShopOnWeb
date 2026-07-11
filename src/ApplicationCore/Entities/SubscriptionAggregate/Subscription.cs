using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class Subscription : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Subscription() { }

    public Subscription(string userId, int maxioCustomerId, int maxioSubscriptionId, int productId, string productHandle)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.Default(maxioCustomerId, nameof(maxioCustomerId));
        Guard.Against.Default(maxioSubscriptionId, nameof(maxioSubscriptionId));
        Guard.Against.Default(productId, nameof(productId));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));

        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        ProductId = productId;
        ProductHandle = productHandle;
        State = SubscriptionState.Active;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string UserId { get; private set; }
    public int MaxioCustomerId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public int ProductId { get; private set; }
    public string ProductHandle { get; private set; }
    public SubscriptionState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public void UpdateState(SubscriptionState newState)
    {
        State = newState;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateProductId(int newProductId, string newProductHandle)
    {
        ProductId = newProductId;
        ProductHandle = newProductHandle;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public enum SubscriptionState
{
    Active,
    Paused,
    Cancelled,
    Pending,
    Trialing,
    PastDue
}
