using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The prorated cost of moving a subscription to another plan, shown to the customer before they
/// commit (plan.md UC3, step 2).
/// </summary>
public sealed record PlanChangePreview
{
    public required int SubscriptionId { get; init; }

    /// <summary>
    /// The plan the subscription is on today. The provider does not echo it back on the preview, so the
    /// orchestrating service fills it in from the subscription it already loaded.
    /// </summary>
    public string? CurrentPlanHandle { get; init; }

    public required string TargetPlanHandle { get; init; }

    public string? TargetPlanName { get; init; }

    /// <summary>Prorated charge for the remainder of the period, in minor units (cents).</summary>
    public long ChargeInCents { get; init; }

    /// <summary>Prorated credit applied from the current plan, in minor units (cents).</summary>
    public long CreditAppliedInCents { get; init; }

    /// <summary>Net amount due immediately, in minor units (cents).</summary>
    public long PaymentDueInCents { get; init; }

    /// <summary>Net proration adjustment, in minor units (cents).</summary>
    public long ProratedAdjustmentInCents { get; init; }

    /// <summary>
    /// When the preview was produced. The commit step refuses a preview that is older than
    /// <see cref="SubscriptionConstants.PreviewValidity"/> so the customer is never charged an amount
    /// other than the one they were shown (plan.md UC3, "preview is stale at commit time").
    /// </summary>
    public required DateTimeOffset PreviewedAt { get; init; }

    public decimal Charge => ChargeInCents / 100m;

    public decimal CreditApplied => CreditAppliedInCents / 100m;

    public decimal PaymentDue => PaymentDueInCents / 100m;

    public decimal ProratedAdjustment => ProratedAdjustmentInCents / 100m;
}
