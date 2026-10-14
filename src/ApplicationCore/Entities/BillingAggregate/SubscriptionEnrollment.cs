using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A user's enrollment in a subscription plan of one product family.
/// The row is inserted (claimed) before the subscription is created in the billing system; its
/// composite primary key (<see cref="UserId"/>, <see cref="ProductFamilyHandle"/>) is what stops a
/// double-submit from creating two subscriptions.
/// </summary>
public class SubscriptionEnrollment : IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string userId, string productFamilyHandle, string planHandle, string reference, DateTimeOffset claimedAt)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Guard.Against.NullOrWhiteSpace(productFamilyHandle, nameof(productFamilyHandle));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        UserId = userId;
        ProductFamilyHandle = productFamilyHandle;
        PlanHandle = planHandle;
        Reference = reference;
        ClaimedAt = claimedAt;
        State = EnrollmentState.Pending;
    }

    public string UserId { get; private set; }

    public string ProductFamilyHandle { get; private set; }

    public string PlanHandle { get; private set; }

    /// <summary>Our own reference for the subscription in the billing system; used to look it up again.</summary>
    public string Reference { get; private set; }

    public EnrollmentState State { get; private set; }

    public int? BillingSubscriptionId { get; private set; }

    public int? BillingCustomerId { get; private set; }

    public DateTimeOffset ClaimedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public void MarkUnknown()
    {
        if (State == EnrollmentState.Pending)
        {
            State = EnrollmentState.Unknown;
        }
    }

    public void MarkActive(int billingSubscriptionId, int? billingCustomerId, DateTimeOffset completedAt)
    {
        Guard.Against.NegativeOrZero(billingSubscriptionId, nameof(billingSubscriptionId));
        State = EnrollmentState.Active;
        BillingSubscriptionId = billingSubscriptionId;
        BillingCustomerId = billingCustomerId;
        CompletedAt = completedAt;
    }
}
