using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The local claim a shopper takes before a subscription is created at the billing provider.
/// Its key is the store's primary key, so a second concurrent request for the same shopper is
/// refused by the store itself before it can reach the provider.
/// </summary>
public class SubscriptionEnrollment : IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string enrollmentKey, string customerReference, string subscriptionReference, string planHandle, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(enrollmentKey, nameof(enrollmentKey));
        Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        Guard.Against.NullOrWhiteSpace(subscriptionReference, nameof(subscriptionReference));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        EnrollmentKey = enrollmentKey;
        CustomerReference = customerReference;
        SubscriptionReference = subscriptionReference;
        PlanHandle = planHandle;
        Status = SubscriptionEnrollmentStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string EnrollmentKey { get; private set; }
    public string CustomerReference { get; private set; }
    public string SubscriptionReference { get; private set; }
    public string PlanHandle { get; private set; }
    public SubscriptionEnrollmentStatus Status { get; private set; }
    public int? BillingCustomerId { get; private set; }
    public int? BillingSubscriptionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic-concurrency token; bumped on every change.</summary>
    public int Version { get; private set; }

    public bool IsCompleted => Status == SubscriptionEnrollmentStatus.Completed;

    /// <summary>
    /// True when nobody can still be working on this claim: the request that took it has had
    /// longer than <paramref name="lease"/> to finish.
    /// </summary>
    public bool IsStale(DateTimeOffset now, TimeSpan lease) => !IsCompleted && now - UpdatedAt > lease;

    public void MarkCompleted(int? billingCustomerId, int billingSubscriptionId, string planHandle, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        BillingCustomerId = billingCustomerId ?? BillingCustomerId;
        BillingSubscriptionId = billingSubscriptionId;
        PlanHandle = planHandle;
        Status = SubscriptionEnrollmentStatus.Completed;
        Touch(now);
    }

    public void MarkOutcomeUnknown(int? billingCustomerId, DateTimeOffset now)
    {
        BillingCustomerId = billingCustomerId ?? BillingCustomerId;
        Status = SubscriptionEnrollmentStatus.OutcomeUnknown;
        Touch(now);
    }

    /// <summary>Takes over a stale claim whose subscription was confirmed absent at the provider.</summary>
    public void Restart(string planHandle, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        PlanHandle = planHandle;
        Status = SubscriptionEnrollmentStatus.Pending;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
