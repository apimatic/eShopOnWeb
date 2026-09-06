using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Subscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public string MaxioCustomerId { get; private set; }
    public string PlanHandle { get; private set; }
    public string State { get; private set; }
    public DateTime? NextBillingAt { get; private set; }
    public decimal PriceInCents { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

#pragma warning disable CS8618
    private Subscription() { }
#pragma warning restore CS8618

    public Subscription(string userId, int maxioSubscriptionId, string maxioCustomerId,
        string planHandle, string state, DateTime? nextBillingAt, decimal priceInCents)
    {
        UserId = userId;
        MaxioSubscriptionId = maxioSubscriptionId;
        MaxioCustomerId = maxioCustomerId;
        PlanHandle = planHandle;
        State = state;
        NextBillingAt = nextBillingAt;
        PriceInCents = priceInCents;
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
