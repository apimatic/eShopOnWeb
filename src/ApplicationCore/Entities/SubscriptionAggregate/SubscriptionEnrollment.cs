using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The local claim a buyer holds while (and after) enrolling in a recurring subscription.
/// The billing provider is the system of record for the subscription itself; this row exists so
/// that two concurrent "subscribe" requests for the same buyer cannot both reach the provider.
/// Its primary key is the buyer id, so the store refuses a second claim.
/// </summary>
public class SubscriptionEnrollment : IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }

    private SubscriptionEnrollment(string buyerId, string planHandle, DateTimeOffset now)
    {
        BuyerId = buyerId;
        PlanHandle = planHandle;
        SubscriptionReference = $"eshop-sub-{Guid.NewGuid():N}";
        Status = EnrollmentStatus.Pending;
        LeaseId = Guid.NewGuid();
        LeaseAcquiredUtc = now;
        CreatedUtc = now;
        UpdatedUtc = now;
    }

    public string BuyerId { get; private set; }
    public string PlanHandle { get; private set; }

    /// <summary>The reference sent with the provider write; used to look the write up when its outcome is unknown.</summary>
    public string SubscriptionReference { get; private set; }

    public EnrollmentStatus Status { get; private set; }

    /// <summary>Concurrency token: changes only when a stale claim is taken over by another request.</summary>
    public Guid LeaseId { get; private set; }
    public DateTimeOffset LeaseAcquiredUtc { get; private set; }

    public int? BillingCustomerId { get; private set; }
    public int? BillingSubscriptionId { get; private set; }

    public DateTimeOffset CreatedUtc { get; private set; }
    public DateTimeOffset UpdatedUtc { get; private set; }

    public static SubscriptionEnrollment Claim(string buyerId, string planHandle, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        return new SubscriptionEnrollment(buyerId, planHandle, now);
    }

    public bool IsLeaseExpired(DateTimeOffset now, TimeSpan leaseDuration) => now - LeaseAcquiredUtc >= leaseDuration;

    /// <summary>
    /// Takes over a stale or unknown claim. The subscription reference is kept, so the earlier attempt's
    /// write (if it landed) can still be found by it.
    /// </summary>
    public void TakeOver(string planHandle, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        PlanHandle = planHandle;
        LeaseId = Guid.NewGuid();
        LeaseAcquiredUtc = now;
        Status = EnrollmentStatus.Pending;
        UpdatedUtc = now;
    }

    public void AttachCustomer(int billingCustomerId, DateTimeOffset now)
    {
        BillingCustomerId = billingCustomerId;
        UpdatedUtc = now;
    }

    public void Complete(int billingSubscriptionId, string planHandle, DateTimeOffset now)
    {
        BillingSubscriptionId = billingSubscriptionId;
        PlanHandle = planHandle;
        Status = EnrollmentStatus.Active;
        UpdatedUtc = now;
    }

    /// <summary>The provider may have acted but we could not confirm it; settled by the next request for this buyer.</summary>
    public void MarkOutcomeUnknown(DateTimeOffset now)
    {
        Status = EnrollmentStatus.OutcomeUnknown;
        UpdatedUtc = now;
    }
}

public enum EnrollmentStatus
{
    Pending = 0,
    Active = 1,
    OutcomeUnknown = 2
}
