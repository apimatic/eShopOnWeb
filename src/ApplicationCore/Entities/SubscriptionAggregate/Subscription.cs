using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum SubscriptionState
{
    Active,
    Paused,
    Cancelled,
    PendingCancellation,
    Reactivating
}

public class Subscription : BaseEntity, IAggregateRoot
{
    private Subscription() { }

    public Subscription(string userId, int billingProviderId, string billingProviderSubscriptionHandle,
        string productHandle, int productId, SubscriptionState state)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Guard.Against.Negative(billingProviderId, nameof(billingProviderId));
        Guard.Against.NullOrWhiteSpace(billingProviderSubscriptionHandle, nameof(billingProviderSubscriptionHandle));
        Guard.Against.NullOrWhiteSpace(productHandle, nameof(productHandle));
        Guard.Against.Negative(productId, nameof(productId));

        UserId = userId;
        BillingProviderId = billingProviderId;
        BillingProviderSubscriptionHandle = billingProviderSubscriptionHandle;
        ProductHandle = productHandle;
        ProductId = productId;
        State = state;
        CreatedAt = DateTime.UtcNow;
    }

    public string UserId { get; set; } = null!;
    public int BillingProviderId { get; set; }
    public string BillingProviderSubscriptionHandle { get; set; } = null!;
    public string ProductHandle { get; set; } = null!;
    public int ProductId { get; set; }
    public SubscriptionState State { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public void UpdateState(SubscriptionState newState)
    {
        State = newState;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateProductAndState(string productHandle, int productId, SubscriptionState state)
    {
        Guard.Against.NullOrWhiteSpace(productHandle, nameof(productHandle));
        Guard.Against.Negative(productId, nameof(productId));

        ProductHandle = productHandle;
        ProductId = productId;
        State = state;
        UpdatedAt = DateTime.UtcNow;
    }
}
