using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class Subscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; } = null!;
    public int MaxioCustomerId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public string ProductHandle { get; private set; } = null!;
    public string State { get; private set; } = null!;
    public decimal CurrentPrice { get; private set; }
    public DateTime? NextBillingAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    #pragma warning disable CS8618
    private Subscription() { }

    public Subscription(string userId, int maxioCustomerId, int maxioSubscriptionId,
        string productHandle, string state, decimal currentPrice, DateTime? nextBillingAt)
    {
        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        ProductHandle = productHandle;
        State = state;
        CurrentPrice = currentPrice;
        NextBillingAt = nextBillingAt;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Update(string state, DateTime? nextBillingAt)
    {
        State = state;
        NextBillingAt = nextBillingAt;
        UpdatedAt = DateTime.UtcNow;
    }
}
