using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Local record of a shopper's enrollment in a billing-provider subscription.
/// One row per user: its primary key (<see cref="UserName"/>) is the claim that stops a
/// double-submitted subscribe from reaching the billing provider twice.
/// </summary>
public class SubscriptionEnrollment
{
    public string UserName { get; private set; }
    public string PlanHandle { get; private set; }
    public string SubscriptionReference { get; private set; }
    public EnrollmentStatus Status { get; private set; }
    public int? BillingCustomerId { get; private set; }
    public int? BillingSubscriptionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic-concurrency token; renewed by the store on every save.</summary>
    public Guid Version { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }

    private SubscriptionEnrollment(string userName, string planHandle, string subscriptionReference, DateTimeOffset now)
    {
        UserName = userName;
        PlanHandle = planHandle;
        SubscriptionReference = subscriptionReference;
        Status = EnrollmentStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    /// <summary>Creates the claim taken before any write reaches the billing provider.</summary>
    public static SubscriptionEnrollment Claim(string userName, string planHandle, string subscriptionReference, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        Guard.Against.NullOrWhiteSpace(subscriptionReference, nameof(subscriptionReference));
        return new SubscriptionEnrollment(userName, planHandle, subscriptionReference, now);
    }

    /// <summary>Re-arms an unsettled claim so this request may enroll (again) for <paramref name="planHandle"/>.</summary>
    public void Restart(string planHandle, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        PlanHandle = planHandle;
        Status = EnrollmentStatus.Pending;
        BillingSubscriptionId = null;
        UpdatedAt = now;
    }

    public void AttachCustomer(int billingCustomerId, DateTimeOffset now)
    {
        BillingCustomerId = billingCustomerId;
        UpdatedAt = now;
    }

    public void MarkActive(int? billingCustomerId, int billingSubscriptionId, string planHandle, DateTimeOffset now)
    {
        BillingCustomerId = billingCustomerId ?? BillingCustomerId;
        BillingSubscriptionId = billingSubscriptionId;
        PlanHandle = planHandle;
        Status = EnrollmentStatus.Enrolled;
        UpdatedAt = now;
    }

    /// <summary>The subscription write may or may not have landed at the provider; it must be settled by re-reading.</summary>
    public void MarkOutcomeUnknown(DateTimeOffset now)
    {
        Status = EnrollmentStatus.OutcomeUnknown;
        UpdatedAt = now;
    }

    /// <summary>A pending claim nobody finished (e.g. the process died mid-request) is treated as unknown.</summary>
    public bool IsStale(DateTimeOffset now, TimeSpan staleAfter) =>
        Status == EnrollmentStatus.Pending && now - UpdatedAt > staleAfter;
}

public enum EnrollmentStatus
{
    Pending = 0,
    Enrolled = 1,
    OutcomeUnknown = 2
}
