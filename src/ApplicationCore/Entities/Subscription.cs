using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class Subscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; } = string.Empty;
    public int MaxioCustomerId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public string ProductHandle { get; private set; } = string.Empty;
    public string SubscriptionState { get; private set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; private set; }
    public decimal PriceInCents { get; private set; }

    #pragma warning disable CS8618
    private Subscription() { }

    public Subscription(string userId, int maxioCustomerId, int maxioSubscriptionId, string productHandle,
        string subscriptionState, decimal priceInCents, DateTimeOffset? nextBillingAt = null)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.Negative(maxioCustomerId, nameof(maxioCustomerId));
        Guard.Against.Negative(maxioSubscriptionId, nameof(maxioSubscriptionId));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));
        Guard.Against.NullOrEmpty(subscriptionState, nameof(subscriptionState));

        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        ProductHandle = productHandle;
        SubscriptionState = subscriptionState;
        PriceInCents = priceInCents;
        NextBillingAt = nextBillingAt;
    }
}
