using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// One buyer's enrollment in one subscription plan. The row is the duplicate-prevention claim for
/// creating the subscription: it is inserted (keyed by <see cref="BuyerId"/> + <see cref="PlanHandle"/>)
/// before the billing system is called, so a double-submit is refused by the primary key.
/// </summary>
public class SubscriptionEnrollment : IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string buyerId, string planHandle, string subscriptionReference, DateTimeOffset claimedAt)
    {
        BuyerId = Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        PlanHandle = Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        SubscriptionReference = Guard.Against.NullOrWhiteSpace(subscriptionReference, nameof(subscriptionReference));
        Status = EnrollmentStatus.Pending;
        ClaimedAt = claimedAt;
        ConcurrencyStamp = Guid.NewGuid();
    }

    public string BuyerId { get; private set; }

    public string PlanHandle { get; private set; }

    /// <summary>The reference sent with the create call; the key used to find the subscription if its outcome is unknown.</summary>
    public string SubscriptionReference { get; private set; }

    public EnrollmentStatus Status { get; private set; }

    public int? BillingSubscriptionId { get; private set; }

    public DateTimeOffset ClaimedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Optimistic concurrency token: changes on every state transition.</summary>
    public Guid ConcurrencyStamp { get; private set; }

    public bool IsClaimFresh(DateTimeOffset now, TimeSpan lease) => Status == EnrollmentStatus.Pending && ClaimedAt + lease > now;

    public void MarkActive(int billingSubscriptionId, DateTimeOffset completedAt)
    {
        BillingSubscriptionId = Guard.Against.NegativeOrZero(billingSubscriptionId, nameof(billingSubscriptionId));
        Status = EnrollmentStatus.Active;
        CompletedAt = completedAt;
        ConcurrencyStamp = Guid.NewGuid();
    }

    public void MarkUnknown()
    {
        Status = EnrollmentStatus.Unknown;
        ConcurrencyStamp = Guid.NewGuid();
    }

    /// <summary>
    /// Re-opens the claim for a new create attempt (after the previous attempt was proven not to have
    /// produced a live subscription). A new reference is issued so the new attempt can be found on its own.
    /// </summary>
    public void Reclaim(string subscriptionReference, DateTimeOffset claimedAt)
    {
        SubscriptionReference = Guard.Against.NullOrWhiteSpace(subscriptionReference, nameof(subscriptionReference));
        Status = EnrollmentStatus.Pending;
        BillingSubscriptionId = null;
        CompletedAt = null;
        ClaimedAt = claimedAt;
        ConcurrencyStamp = Guid.NewGuid();
    }
}
