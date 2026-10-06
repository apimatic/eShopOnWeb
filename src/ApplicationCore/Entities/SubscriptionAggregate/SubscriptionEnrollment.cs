using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Links an eShop shopper to their subscription in the billing system.
/// The row doubles as the duplicate-prevention claim: <see cref="BuyerId"/> is the primary key, so a second
/// concurrent subscribe request for the same shopper is refused by the store before it reaches the provider.
/// </summary>
public class SubscriptionEnrollment : IAggregateRoot
{
    public string BuyerId { get; private set; }
    public string PlanHandle { get; private set; }

    /// <summary>
    /// Unique reference sent to the billing system with the create call; used to find the subscription
    /// again when the outcome of that call is unknown.
    /// </summary>
    public string SubscriptionReference { get; private set; }
    public SubscriptionEnrollmentStatus Status { get; private set; }
    public int? BillingCustomerId { get; private set; }
    public int? BillingSubscriptionId { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string buyerId, string planHandle, string subscriptionReference, DateTimeOffset claimedAt)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));
        Guard.Against.NullOrEmpty(subscriptionReference, nameof(subscriptionReference));

        BuyerId = buyerId;
        PlanHandle = planHandle;
        SubscriptionReference = subscriptionReference;
        ClaimedAt = claimedAt;
        Status = SubscriptionEnrollmentStatus.Pending;
    }

    public void AssignBillingCustomer(int billingCustomerId)
    {
        BillingCustomerId = billingCustomerId;
    }

    public void MarkCompleted(int billingCustomerId, int billingSubscriptionId, string planHandle, DateTimeOffset completedAt)
    {
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));

        BillingCustomerId = billingCustomerId;
        BillingSubscriptionId = billingSubscriptionId;
        PlanHandle = planHandle;
        Status = SubscriptionEnrollmentStatus.Completed;
        CompletedAt = completedAt;
    }
}
