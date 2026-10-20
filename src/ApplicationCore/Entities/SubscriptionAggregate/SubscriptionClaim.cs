using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum SubscriptionClaimState
{
    /// <summary>Claim taken; the billing provider calls are in flight.</summary>
    Pending = 0,
    /// <summary>A write may or may not have landed at the billing provider; settled by re-reading it.</summary>
    Unknown = 1,
    Completed = 2
}

/// <summary>
/// One row per shopper: the claim that makes subscribing idempotent (the user id is the primary key, so a
/// second concurrent claim is refused by the store) and the local userId to billing-provider id mapping.
/// </summary>
public class SubscriptionClaim : IAggregateRoot
{
    public string UserId { get; private set; }
    public SubscriptionClaimState State { get; private set; }
    public string PlanHandle { get; private set; }
    public int? CustomerId { get; private set; }
    public int? SubscriptionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    /// <summary>Concurrency token: changes on every update so that two recoverers cannot both take over.</summary>
    public Guid Version { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionClaim() { }

    public SubscriptionClaim(string userId, string planHandle, DateTimeOffset now)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));
        UserId = userId;
        PlanHandle = planHandle;
        State = SubscriptionClaimState.Pending;
        CreatedAt = UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    public void MarkPending(string planHandle, DateTimeOffset now) => Transition(SubscriptionClaimState.Pending, now, planHandle);
    public void MarkUnknown(DateTimeOffset now) => Transition(SubscriptionClaimState.Unknown, now);

    public void SetCustomer(int customerId, DateTimeOffset now)
    {
        CustomerId = customerId;
        Transition(State, now);
    }

    public void MarkCompleted(int? customerId, int? subscriptionId, DateTimeOffset now)
    {
        CustomerId = customerId ?? CustomerId;
        SubscriptionId = subscriptionId;
        Transition(SubscriptionClaimState.Completed, now);
    }

    private void Transition(SubscriptionClaimState state, DateTimeOffset now, string? planHandle = null)
    {
        State = state;
        if (planHandle != null) PlanHandle = planHandle;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}
