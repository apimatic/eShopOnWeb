using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public static class SubscriptionEnrollmentStatus
{
    public const string Subscribing = "Subscribing";
    public const string Active = "Active";
    public const string Failed = "Failed";
}

/// <summary>
/// Local (eShopOnWeb-side) record of a Maxio subscription enrollment for a shopper.
///
/// Maxio Advanced Billing is the system of record for the subscription itself; this row is the
/// idempotency gate that makes a double-click (or a concurrent duplicate request) resolve to a
/// single Maxio subscription, and it maps a shopper + plan back to the Maxio customer/subscription
/// handles. A unique index on (BuyerId, PlanHandle) is the cross-instance backstop for the gate.
/// </summary>
public class SubscriptionEnrollment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string buyerId, string planHandle, string customerReference)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));

        BuyerId = buyerId;
        PlanHandle = planHandle;
        CustomerReference = customerReference;
    }

    public string BuyerId { get; private set; }
    public string PlanHandle { get; private set; }
    public string CustomerReference { get; private set; }
    public int? MaxioCustomerId { get; private set; }
    public string? MaxioSubscriptionReference { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public string? State { get; private set; }
    public long? ProductPriceInCents { get; private set; }
    public DateTimeOffset? NextBillingDate { get; private set; }
    public string Status { get; private set; } = SubscriptionEnrollmentStatus.Subscribing;
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Marks this row as the in-flight claim for a subscription being created on Maxio.
    /// </summary>
    public void MarkSubscribing(int? maxioCustomerId)
    {
        MaxioCustomerId = maxioCustomerId;
        Status = SubscriptionEnrollmentStatus.Subscribing;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Records the Maxio subscription created for this enrollment.
    /// </summary>
    public void MarkSubscribed(int? maxioSubscriptionId, string? maxioSubscriptionReference,
        string? state, long? productPriceInCents, DateTimeOffset? nextBillingDate)
    {
        MaxioSubscriptionId = maxioSubscriptionId;
        MaxioSubscriptionReference = maxioSubscriptionReference;
        State = state;
        ProductPriceInCents = productPriceInCents;
        NextBillingDate = nextBillingDate;
        Status = SubscriptionEnrollmentStatus.Active;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string? state = null)
    {
        Status = SubscriptionEnrollmentStatus.Failed;
        State = state;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Refreshes the snapshot of the Maxio subscription this row maps to (used on read-back).
    /// </summary>
    public void SyncSnapshot(string? state, long? productPriceInCents, DateTimeOffset? nextBillingDate)
    {
        State = state;
        ProductPriceInCents = productPriceInCents;
        NextBillingDate = nextBillingDate;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
