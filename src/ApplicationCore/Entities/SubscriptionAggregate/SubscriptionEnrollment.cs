using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// One row per eShopOnWeb user that has started enrolling in a recurring subscription.
/// The primary key is the user's identity id, so the store itself refuses a second concurrent
/// enrollment for the same user before anything reaches the billing provider (the "claim").
/// </summary>
public class SubscriptionEnrollment : IAggregateRoot
{
    public string BuyerId { get; private set; }
    public SubscriptionEnrollmentStatus Status { get; private set; }
    public string PlanHandle { get; private set; }

    /// <summary>The reference sent to the billing provider with the subscription create; used to find it again.</summary>
    public string SubscriptionReference { get; private set; }
    public int? BillingCustomerId { get; private set; }
    public int? BillingSubscriptionId { get; private set; }
    public DateTimeOffset AttemptStartedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic-concurrency token: every state change issues a new value.</summary>
    public Guid Version { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string buyerId, string planHandle, string subscriptionReference, DateTimeOffset now)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        BuyerId = buyerId;
        BeginAttempt(planHandle, subscriptionReference, now);
    }

    public bool IsAwaitingConfirmation => Status is SubscriptionEnrollmentStatus.Pending or SubscriptionEnrollmentStatus.Unknown;

    /// <summary>A pending attempt older than <paramref name="staleAfter"/> was abandoned (e.g. the process died mid-call).</summary>
    public bool IsStale(DateTimeOffset now, TimeSpan staleAfter) =>
        Status == SubscriptionEnrollmentStatus.Pending && now - UpdatedAt > staleAfter;

    /// <summary>Starts a brand-new subscribe attempt (first enrollment, or re-subscribing after the previous subscription ended).</summary>
    public void BeginAttempt(string planHandle, string subscriptionReference, DateTimeOffset now)
    {
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));
        Guard.Against.NullOrEmpty(subscriptionReference, nameof(subscriptionReference));
        PlanHandle = planHandle;
        SubscriptionReference = subscriptionReference;
        BillingSubscriptionId = null;
        AttemptStartedAt = now;
        Touch(SubscriptionEnrollmentStatus.Pending, now);
    }

    /// <summary>Takes over an attempt whose outcome was never settled; plan and reference stay as they were sent.</summary>
    public void ResumeAttempt(DateTimeOffset now) => Touch(SubscriptionEnrollmentStatus.Pending, now);

    public void RecordBillingCustomer(int billingCustomerId, DateTimeOffset now)
    {
        BillingCustomerId = billingCustomerId;
        Touch(Status, now);
    }

    public void MarkActive(int billingSubscriptionId, string planHandle, DateTimeOffset now)
    {
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));
        BillingSubscriptionId = billingSubscriptionId;
        PlanHandle = planHandle;
        Touch(SubscriptionEnrollmentStatus.Active, now);
    }

    /// <summary>The provider may or may not have created the subscription; the next request re-reads and settles it.</summary>
    public void MarkUnknown(DateTimeOffset now) => Touch(SubscriptionEnrollmentStatus.Unknown, now);

    private void Touch(SubscriptionEnrollmentStatus status, DateTimeOffset now)
    {
        Status = status;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}
