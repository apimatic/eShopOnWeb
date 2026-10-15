using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Local record of a shopper's enrollment in a billing-provider subscription.
/// The billing provider is the system of record for the subscription itself; this row is the
/// duplicate-prevention claim (its primary key is <see cref="ClaimKey"/>) taken before any provider write,
/// plus the identifiers needed to settle a write whose outcome is unknown.
/// </summary>
public class SubscriptionEnrollment : IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string customerReference, string productFamilyHandle, string userName,
        string planHandle, string subscriptionReference, DateTimeOffset nowUtc)
    {
        Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        Guard.Against.NullOrWhiteSpace(productFamilyHandle, nameof(productFamilyHandle));
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        Guard.Against.NullOrWhiteSpace(subscriptionReference, nameof(subscriptionReference));

        ClaimKey = CreateClaimKey(customerReference, productFamilyHandle);
        CustomerReference = customerReference;
        ProductFamilyHandle = productFamilyHandle;
        UserName = userName;
        PlanHandle = planHandle;
        SubscriptionReference = subscriptionReference;
        Status = SubscriptionEnrollmentStatus.Pending;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        ConcurrencyStamp = Guid.NewGuid();
    }

    /// <summary>One enrollment per customer per product family — the store rejects a second claim on this key.</summary>
    public string ClaimKey { get; private set; }
    public string CustomerReference { get; private set; }
    public string ProductFamilyHandle { get; private set; }
    public string UserName { get; private set; }
    public string PlanHandle { get; private set; }

    /// <summary>The reference sent with the provider's create call; used to find that subscription again.</summary>
    public string SubscriptionReference { get; private set; }
    public SubscriptionEnrollmentStatus Status { get; private set; }
    public int? BillingCustomerId { get; private set; }
    public int? BillingSubscriptionId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Optimistic-concurrency token: two callers cannot both take over the same stale claim.</summary>
    public Guid ConcurrencyStamp { get; private set; }

    public static string CreateClaimKey(string customerReference, string productFamilyHandle) =>
        $"{customerReference}|{productFamilyHandle}";

    public bool IsPendingLongerThan(TimeSpan threshold, DateTimeOffset nowUtc) =>
        Status == SubscriptionEnrollmentStatus.Pending && nowUtc - UpdatedAtUtc > threshold;

    public void MarkActive(int billingCustomerId, int billingSubscriptionId, string planHandle, DateTimeOffset nowUtc)
    {
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        BillingCustomerId = billingCustomerId;
        BillingSubscriptionId = billingSubscriptionId;
        PlanHandle = planHandle;
        Status = SubscriptionEnrollmentStatus.Active;
        Touch(nowUtc);
    }

    /// <summary>Records a live subscription that already existed at the provider (e.g. after local state was lost).</summary>
    public void Adopt(int billingCustomerId, int billingSubscriptionId, string planHandle, string? subscriptionReference, DateTimeOffset nowUtc)
    {
        if (!string.IsNullOrWhiteSpace(subscriptionReference))
        {
            SubscriptionReference = subscriptionReference;
        }
        MarkActive(billingCustomerId, billingSubscriptionId, planHandle, nowUtc);
    }

    public void RecordCustomer(int billingCustomerId, DateTimeOffset nowUtc)
    {
        BillingCustomerId = billingCustomerId;
        Touch(nowUtc);
    }

    /// <summary>Re-arms a stale pending claim for a new attempt. The subscription reference is kept, so a
    /// create that landed late is still found by it.</summary>
    public void Retake(string planHandle, DateTimeOffset nowUtc)
    {
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        if (Status != SubscriptionEnrollmentStatus.Pending)
        {
            throw new InvalidOperationException("Only a pending enrollment can be retaken.");
        }
        PlanHandle = planHandle;
        Touch(nowUtc);
    }

    private void Touch(DateTimeOffset nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        ConcurrencyStamp = Guid.NewGuid();
    }
}
