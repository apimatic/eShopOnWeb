using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Local record of a shopper's enrollment in a recurring plan. The billing provider is the system of
/// record for the subscription itself; this row is the claim that keeps one shopper from being enrolled
/// twice (its key is the shopper's stable billing reference) and remembers provider ids once known.
/// </summary>
public class SubscriptionEnrollment
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string userReference, string userName, string planHandle, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        UserReference = userReference;
        UserName = userName;
        PlanHandle = planHandle;
        Status = SubscriptionEnrollmentStatus.Pending;
        ClaimToken = Guid.NewGuid();
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Primary key: deterministic per shopper, so a second claim for the same shopper is refused.</summary>
    public string UserReference { get; private set; }
    public string UserName { get; private set; }
    public string PlanHandle { get; private set; }
    public SubscriptionEnrollmentStatus Status { get; private set; }
    public int? BillingCustomerId { get; private set; }
    public int? BillingSubscriptionId { get; private set; }

    /// <summary>Concurrency token: changes every time a caller takes the claim over.</summary>
    public Guid ClaimToken { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsStale(DateTimeOffset now, TimeSpan pendingTimeout) =>
        Status == SubscriptionEnrollmentStatus.Pending && now - UpdatedAt > pendingTimeout;

    /// <summary>Re-claims a stale or unknown enrollment for a new attempt.</summary>
    public void TakeOver(string planHandle, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        PlanHandle = planHandle;
        Status = SubscriptionEnrollmentStatus.Pending;
        ClaimToken = Guid.NewGuid();
        UpdatedAt = now;
    }

    public void RecordCustomer(int customerId, DateTimeOffset now)
    {
        BillingCustomerId = customerId;
        UpdatedAt = now;
    }

    public void MarkActive(int subscriptionId, int? customerId, string planHandle, DateTimeOffset now)
    {
        BillingSubscriptionId = subscriptionId;
        BillingCustomerId = customerId ?? BillingCustomerId;
        PlanHandle = planHandle;
        Status = SubscriptionEnrollmentStatus.Active;
        UpdatedAt = now;
    }

    /// <summary>The provider may or may not have acted; a later call settles it by re-reading the provider.</summary>
    public void MarkUnknown(DateTimeOffset now)
    {
        Status = SubscriptionEnrollmentStatus.Unknown;
        UpdatedAt = now;
    }
}

public enum SubscriptionEnrollmentStatus
{
    Pending = 0,
    Active = 1,
    Unknown = 2
}
