using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public enum SubscriptionEnrollmentStatus
{
    /// <summary>A subscribe call holds the claim and may be talking to the billing provider right now.</summary>
    Claimed = 0,

    /// <summary>The billing provider confirmed the subscription.</summary>
    Active = 1,

    /// <summary>The create call may have landed at the provider but could not be confirmed; settled by a later re-read.</summary>
    Unknown = 2
}

/// <summary>
/// The local claim that guards the subscribe flow against duplicates. Its primary key is the eShop
/// user name, so the store itself refuses a second concurrent subscribe for the same user. The claim
/// is inserted before any billing-provider write and released when the provider definitively refuses.
/// </summary>
public class SubscriptionEnrollment
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }
    #pragma warning restore CS8618

    public SubscriptionEnrollment(string userName, string planHandle, string subscriptionReference, DateTimeOffset nowUtc)
    {
        UserName = userName;
        PlanHandle = planHandle;
        SubscriptionReference = subscriptionReference;
        Status = SubscriptionEnrollmentStatus.Claimed;
        ClaimedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Primary key: the eShop user name (also the billing customer reference).</summary>
    public string UserName { get; private set; }

    /// <summary>The plan handle the claim was taken for.</summary>
    public string PlanHandle { get; private set; }

    /// <summary>The deterministic subscription reference sent to the provider on every create attempt.</summary>
    public string SubscriptionReference { get; private set; }

    public int? BillingCustomerId { get; private set; }

    public int? BillingSubscriptionId { get; private set; }

    public SubscriptionEnrollmentStatus Status { get; private set; }

    public DateTimeOffset ClaimedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void RecordCustomer(int customerId, DateTimeOffset nowUtc)
    {
        BillingCustomerId = customerId;
        UpdatedAtUtc = nowUtc;
    }

    /// <param name="subscriptionId">The provider's subscription id.</param>
    /// <param name="nowUtc">Current time.</param>
    /// <param name="planHandle">
    /// The plan the provider actually holds, when it differs from the one the claim was taken for
    /// (an existing subscription found during reconciliation).
    /// </param>
    public void MarkActive(int? subscriptionId, DateTimeOffset nowUtc, string? planHandle = null)
    {
        if (!string.IsNullOrWhiteSpace(planHandle))
        {
            PlanHandle = planHandle;
        }

        BillingSubscriptionId = subscriptionId;
        Status = SubscriptionEnrollmentStatus.Active;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkUnknown(DateTimeOffset nowUtc)
    {
        Status = SubscriptionEnrollmentStatus.Unknown;
        UpdatedAtUtc = nowUtc;
    }
}
