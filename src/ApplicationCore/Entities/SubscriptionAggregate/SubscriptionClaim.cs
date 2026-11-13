using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// One row per (customer, plan) the user has asked to subscribe to. It is inserted BEFORE the billing
/// system is asked to create the subscription; its composite primary key makes the store refuse a second
/// concurrent request (double-click, client retry, two instances) for the same user and plan.
/// </summary>
public class SubscriptionClaim : IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionClaim() { }
    #pragma warning restore CS8618

    public SubscriptionClaim(string customerReference, string planHandle, string subscriptionReference, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        Guard.Against.NullOrWhiteSpace(subscriptionReference, nameof(subscriptionReference));
        CustomerReference = customerReference;
        PlanHandle = planHandle;
        SubscriptionReference = subscriptionReference;
        Status = SubscriptionClaimStatus.Pending;
        ClaimToken = Guid.NewGuid();
        ClaimedAt = now;
        UpdatedAt = now;
    }

    public string CustomerReference { get; private set; }

    public string PlanHandle { get; private set; }

    /// <summary>
    /// The reference sent to the billing system with the create call; used to look the subscription up
    /// when the outcome of the create is unknown (timeout, dropped connection, 5xx).
    /// </summary>
    public string SubscriptionReference { get; private set; }

    public SubscriptionClaimStatus Status { get; private set; }

    public int? BillingSubscriptionId { get; private set; }

    /// <summary>Concurrency token: changes every time the claim changes hands or state.</summary>
    public Guid ClaimToken { get; private set; }

    public DateTimeOffset ClaimedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsInFlight(DateTimeOffset now, TimeSpan lease) =>
        Status == SubscriptionClaimStatus.Pending && now - ClaimedAt <= lease;

    /// <param name="providerReference">
    /// When adopting a subscription that was created elsewhere, its own reference (if it has one) replaces
    /// ours so later look-ups by reference find it.
    /// </param>
    public void MarkCreated(int billingSubscriptionId, DateTimeOffset now, string? providerReference = null)
    {
        if (!string.IsNullOrWhiteSpace(providerReference))
        {
            SubscriptionReference = providerReference;
        }
        BillingSubscriptionId = billingSubscriptionId;
        Status = SubscriptionClaimStatus.Created;
        ClaimToken = Guid.NewGuid();
        UpdatedAt = now;
    }

    /// <summary>The create call's outcome could not be established; a later request settles it.</summary>
    public void MarkUnknown(DateTimeOffset now)
    {
        Status = SubscriptionClaimStatus.Unknown;
        ClaimToken = Guid.NewGuid();
        UpdatedAt = now;
    }

    /// <summary>
    /// Takes the claim over for a new create attempt. Pass the same reference when retrying an attempt whose
    /// outcome was unknown, or a new one when the previous subscription has ended.
    /// </summary>
    public void Reclaim(string subscriptionReference, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(subscriptionReference, nameof(subscriptionReference));
        SubscriptionReference = subscriptionReference;
        BillingSubscriptionId = null;
        Status = SubscriptionClaimStatus.Pending;
        ClaimToken = Guid.NewGuid();
        ClaimedAt = now;
        UpdatedAt = now;
    }
}

public enum SubscriptionClaimStatus
{
    /// <summary>A create call is (or was) in flight.</summary>
    Pending = 0,
    /// <summary>The billing system confirmed the subscription.</summary>
    Created = 1,
    /// <summary>The create call failed in a way that leaves its outcome unknown.</summary>
    Unknown = 2
}
