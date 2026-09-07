using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class Subscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public int MaxioCustomerId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public string SubscriptionReference { get; private set; }
    public string PlanHandle { get; private set; }
    public string PlanName { get; private set; }
    public long PriceInCents { get; private set; }
    public string State { get; private set; }
    public DateTimeOffset? NextAssessmentAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    #pragma warning disable CS8618
    private Subscription() { }

    public Subscription(
        string userId,
        int maxioCustomerId,
        int maxioSubscriptionId,
        string subscriptionReference,
        string planHandle,
        string planName,
        long priceInCents,
        string state,
        DateTimeOffset? nextAssessmentAt) : this()
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));
        Guard.Against.NullOrEmpty(planName, nameof(planName));
        Guard.Against.NullOrEmpty(state, nameof(state));

        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        SubscriptionReference = subscriptionReference;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        State = state;
        NextAssessmentAt = nextAssessmentAt;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateFromMaxio(string state, DateTimeOffset? nextAssessmentAt)
    {
        State = state;
        NextAssessmentAt = nextAssessmentAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
