using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class Subscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public int MaxioCustomerId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public string ProductHandle { get; private set; }
    public string State { get; private set; }
    public decimal CurrentPrice { get; private set; }
    public DateTime NextBillingAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Subscription()
    {
        UserId = string.Empty;
        ProductHandle = string.Empty;
        State = string.Empty;
    }

    public Subscription(string userId, int maxioCustomerId, int maxioSubscriptionId,
        string productHandle, string state, decimal currentPrice, DateTime nextBillingAt)
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

    public void Update(string state, decimal currentPrice, DateTime nextBillingAt)
    {
        State = state;
        CurrentPrice = currentPrice;
        NextBillingAt = nextBillingAt;
        UpdatedAt = DateTime.UtcNow;
    }
}
