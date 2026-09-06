using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class Subscription : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Subscription() { }

    public Subscription(string userId, int maxioSubscriptionId, string productHandle, string state)
    {
        UserId = userId;
        MaxioSubscriptionId = maxioSubscriptionId;
        ProductHandle = productHandle;
        State = state;
    }

    public string UserId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public string ProductHandle { get; private set; }
    public string State { get; private set; }
    public long? ProductPriceInCents { get; private set; }
    public DateTimeOffset? NextAssessmentAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void UpdateFromMaxio(string state, long? priceInCents, DateTimeOffset? nextAssessmentAt)
    {
        State = state;
        ProductPriceInCents = priceInCents;
        NextAssessmentAt = nextAssessmentAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
