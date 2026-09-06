using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class Subscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public string PlanHandle { get; private set; }
    public decimal PlanPrice { get; private set; }
    public string PlanName { get; private set; }
    public SubscriptionState State { get; private set; }
    public DateTime CreatedDate { get; private set; }
    public DateTime? NextBillingDate { get; private set; }
    public DateTime? CanceledDate { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private Subscription() { }
#pragma warning restore CS8618

    public Subscription(string userId, int maxioSubscriptionId, string planHandle, decimal planPrice, string planName, SubscriptionState state, DateTime nextBillingDate)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.Negative(maxioSubscriptionId, nameof(maxioSubscriptionId));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));
        Guard.Against.Negative((int)planPrice, nameof(planPrice));
        Guard.Against.NullOrEmpty(planName, nameof(planName));

        UserId = userId;
        MaxioSubscriptionId = maxioSubscriptionId;
        PlanHandle = planHandle;
        PlanPrice = planPrice;
        PlanName = planName;
        State = state;
        CreatedDate = DateTime.UtcNow;
        NextBillingDate = nextBillingDate;
    }

    public void Cancel()
    {
        State = SubscriptionState.Canceled;
        CanceledDate = DateTime.UtcNow;
    }
}

public enum SubscriptionState
{
    Active,
    Paused,
    Pending,
    Canceled,
    Expired,
    Trialing,
    AwaitingSignup,
    Assigning
}
