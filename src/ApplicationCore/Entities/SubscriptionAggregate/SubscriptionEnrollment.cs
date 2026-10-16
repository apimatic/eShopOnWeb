using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A buyer's enrollment in a plan of the configured product family.
/// The row is written before the billing system is called and its key — buyer, product family and generation —
/// is what makes a double-submit harmless: the store refuses the second insert, so only one request reaches the
/// billing system. A generation ends when its subscription is canceled or expired, which frees the next one.
/// </summary>
public class SubscriptionEnrollment : IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string buyerId,
        string productFamilyHandle,
        int generation,
        string planHandle,
        DateTimeOffset claimedAt)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Guard.Against.NullOrWhiteSpace(productFamilyHandle, nameof(productFamilyHandle));
        Guard.Against.Negative(generation, nameof(generation));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        ClaimKey = BuildClaimKey(buyerId, productFamilyHandle, generation);
        BuyerId = buyerId;
        ProductFamilyHandle = productFamilyHandle;
        Generation = generation;
        PlanHandle = planHandle;
        SubscriptionReference = BillingReferences.ForSubscription(ClaimKey);
        Status = EnrollmentStatus.Pending;
        ClaimedAt = claimedAt;
        UpdatedAt = claimedAt;
    }

    public string ClaimKey { get; private set; }
    public string BuyerId { get; private set; }
    public string ProductFamilyHandle { get; private set; }
    public int Generation { get; private set; }
    public string PlanHandle { get; private set; }

    /// <summary>The reference sent with the subscription; the key used to find it again when an outcome is unknown.</summary>
    public string SubscriptionReference { get; private set; }

    public EnrollmentStatus Status { get; private set; }
    public int? BillingSubscriptionId { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static string BuildClaimKey(string buyerId, string productFamilyHandle, int generation) =>
        $"{buyerId}|{productFamilyHandle}|{generation}";

    public void MarkActive(int billingSubscriptionId, string? planHandle, DateTimeOffset now)
    {
        Guard.Against.NegativeOrZero(billingSubscriptionId, nameof(billingSubscriptionId));
        BillingSubscriptionId = billingSubscriptionId;
        if (!string.IsNullOrWhiteSpace(planHandle)) PlanHandle = planHandle;
        Status = EnrollmentStatus.Active;
        UpdatedAt = now;
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = EnrollmentStatus.Unknown;
        UpdatedAt = now;
    }

    public void MarkEnded(int? billingSubscriptionId, DateTimeOffset now)
    {
        if (billingSubscriptionId is > 0) BillingSubscriptionId = billingSubscriptionId;
        Status = EnrollmentStatus.Ended;
        UpdatedAt = now;
    }

    /// <summary>
    /// True when the billing outcome of this enrollment has not been confirmed: the create call's result was lost,
    /// or the request that held the claim never finished within <paramref name="pendingLease"/>.
    /// </summary>
    public bool NeedsSettlement(DateTimeOffset now, TimeSpan pendingLease) =>
        Status == EnrollmentStatus.Unknown ||
        (Status == EnrollmentStatus.Pending && now - ClaimedAt >= pendingLease);
}
