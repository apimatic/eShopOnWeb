using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class Subscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; } = string.Empty;
    public int MaxioCustomerId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public string ProductHandle { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;
    public DateTime? CurrentPeriodEndsAt { get; private set; }
    public decimal PriceInCents { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private Subscription() { }

    public Subscription(string userId, int maxioCustomerId, int maxioSubscriptionId,
        string productHandle, string state, decimal priceInCents, DateTime? currentPeriodEndsAt)
    {
        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        ProductHandle = productHandle;
        State = state;
        PriceInCents = priceInCents;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Update(string state, DateTime? currentPeriodEndsAt)
    {
        State = state;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        UpdatedAt = DateTime.UtcNow;
    }
}
