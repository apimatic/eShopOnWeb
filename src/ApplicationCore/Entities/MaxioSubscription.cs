using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class MaxioSubscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public string PlanHandle { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public int MaxioCustomerId { get; private set; }
    public string? State { get; private set; }
    public long? PriceInCents { get; private set; }
    public string? Currency { get; private set; }
    public DateTimeOffset? NextBillingAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public MaxioSubscription(string userId,
        string planHandle,
        int maxioSubscriptionId,
        int maxioCustomerId,
        string? state,
        long? priceInCents,
        string? currency,
        DateTimeOffset? nextBillingAt)
    {
        UserId = userId;
        PlanHandle = planHandle;
        MaxioSubscriptionId = maxioSubscriptionId;
        MaxioCustomerId = maxioCustomerId;
        State = state;
        PriceInCents = priceInCents;
        Currency = currency;
        NextBillingAt = nextBillingAt;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateFromMaxio(int maxioSubscriptionId,
        int maxioCustomerId,
        string? state,
        long? priceInCents,
        string? currency,
        DateTimeOffset? nextBillingAt)
    {
        MaxioSubscriptionId = maxioSubscriptionId;
        MaxioCustomerId = maxioCustomerId;
        State = state;
        PriceInCents = priceInCents;
        Currency = currency;
        NextBillingAt = nextBillingAt;
    }
}