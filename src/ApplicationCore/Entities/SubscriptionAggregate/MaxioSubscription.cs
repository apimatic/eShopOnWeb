using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Local mirror of a Maxio subscription enrollment, binding an eShopOnWeb user
/// to the customer / subscription records held by the billing provider.
/// Maxio remains the billing system of record; this record exists so the shop can
/// quickly resolve "which Maxio customer is this user" and audit enrollments.
/// </summary>
public class MaxioSubscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public int MaxioCustomerId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public string PlanHandle { get; private set; }
    public string PlanName { get; private set; }
    public decimal Price { get; private set; }
    public string IntervalUnit { get; private set; }
    public string State { get; private set; }
    public DateTimeOffset? NextBillingAt { get; private set; }
    public DateTimeOffset SubscribedAt { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private MaxioSubscription() { }

    public MaxioSubscription(string userId,
        int maxioCustomerId,
        int maxioSubscriptionId,
        string planHandle,
        string planName,
        decimal price,
        string intervalUnit,
        string state,
        DateTimeOffset? nextBillingAt,
        DateTimeOffset subscribedAt)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Guard.Against.NegativeOrZero(maxioCustomerId, nameof(maxioCustomerId));
        Guard.Against.NegativeOrZero(maxioSubscriptionId, nameof(maxioSubscriptionId));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        Guard.Against.NullOrWhiteSpace(planName, nameof(planName));
        Guard.Against.NullOrWhiteSpace(state, nameof(state));

        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        PlanHandle = planHandle;
        PlanName = planName;
        Price = price;
        IntervalUnit = intervalUnit;
        State = state;
        NextBillingAt = nextBillingAt;
        SubscribedAt = subscribedAt;
    }

    public void SyncFromBillingProvider(string state, DateTimeOffset? nextBillingAt)
    {
        Guard.Against.NullOrWhiteSpace(state, nameof(state));
        State = state;
        NextBillingAt = nextBillingAt;
    }
}
