using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

public enum ClaimState
{
    Claimed = 0,
    Completed = 1,
    /// <summary>The write may or may not have reached the billing provider; settle by re-reading it.</summary>
    Unknown = 2
}

/// <summary>
/// Claim on "user X subscribes to plan Y". The composite primary key (UserId, PlanHandle) is what refuses
/// a second, concurrent subscribe request before any call is made to the billing provider.
/// </summary>
public class SubscriptionClaim : IAggregateRoot
{
    public string UserId { get; private set; }
    public string PlanHandle { get; private set; }
    public ClaimState State { get; private set; }
    public int? ProviderSubscriptionId { get; private set; }
    public DateTime ClaimedAtUtc { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionClaim() { }
#pragma warning restore CS8618

    public SubscriptionClaim(string userId, string planHandle, DateTime claimedAtUtc)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        UserId = userId;
        PlanHandle = planHandle;
        ClaimedAtUtc = claimedAtUtc;
        State = ClaimState.Claimed;
    }

    public void Complete(int providerSubscriptionId)
    {
        State = ClaimState.Completed;
        ProviderSubscriptionId = providerSubscriptionId;
    }

    public void MarkUnknown() => State = ClaimState.Unknown;
}
