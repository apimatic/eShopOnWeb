using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A buyer's enrollment in one subscription plan. The row is written (as a claim) before the billing
/// system is called; its composite primary key (buyer, plan) makes the store refuse a second concurrent
/// enrollment, so a double-submit can never create two subscriptions.
/// </summary>
public class SubscriptionEnrollment : IAggregateRoot
{
    public string BuyerId { get; private set; }
    public string PlanHandle { get; private set; }

    /// <summary>App-generated reference sent with the create call; used to find the subscription if the outcome is unknown.</summary>
    public string SubscriptionReference { get; private set; }
    public EnrollmentStatus Status { get; private set; }
    public int? BillingSubscriptionId { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string buyerId, string planHandle, string subscriptionReference, DateTimeOffset claimedAt) : this()
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        Guard.Against.NullOrWhiteSpace(subscriptionReference, nameof(subscriptionReference));
        BuyerId = buyerId;
        PlanHandle = planHandle;
        SubscriptionReference = subscriptionReference;
        Status = EnrollmentStatus.Pending;
        ClaimedAt = claimedAt;
        UpdatedAt = claimedAt;
    }

    public void MarkActive(int billingSubscriptionId, DateTimeOffset now)
    {
        Guard.Against.NegativeOrZero(billingSubscriptionId, nameof(billingSubscriptionId));
        BillingSubscriptionId = billingSubscriptionId;
        Status = EnrollmentStatus.Active;
        UpdatedAt = now;
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = EnrollmentStatus.Unknown;
        UpdatedAt = now;
    }

    /// <summary>True when the claim is old enough that no request can still be working on it.</summary>
    public bool IsStale(DateTimeOffset now, TimeSpan staleAfter) => now - ClaimedAt > staleAfter;
}
