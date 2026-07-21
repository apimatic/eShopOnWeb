using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A proration preview for a "now" plan change. The billing provider's preview response carries no
/// effective-date field, so <see cref="EffectiveAt"/> is tracked client-side (the moment the preview
/// was generated).
/// </summary>
public class BillingPlanChangePreview
{
    public BillingPlanChangePreview(
        string targetPlanHandle,
        long proratedAdjustmentInCents,
        long chargeInCents,
        long paymentDueInCents,
        long creditAppliedInCents,
        DateTimeOffset effectiveAt)
    {
        TargetPlanHandle = targetPlanHandle;
        ProratedAdjustmentInCents = proratedAdjustmentInCents;
        ChargeInCents = chargeInCents;
        PaymentDueInCents = paymentDueInCents;
        CreditAppliedInCents = creditAppliedInCents;
        EffectiveAt = effectiveAt;
    }

    public string TargetPlanHandle { get; }
    public long ProratedAdjustmentInCents { get; }
    public long ChargeInCents { get; }
    public long PaymentDueInCents { get; }
    public long CreditAppliedInCents { get; }
    public DateTimeOffset EffectiveAt { get; }

    /// <summary>
    /// Whether this preview's amounts still match a freshly recomputed one, used to reject a stale
    /// commit rather than silently applying a different amount than the one previewed.
    /// </summary>
    public bool MatchesAmounts(BillingPlanChangePreview other) =>
        TargetPlanHandle == other.TargetPlanHandle &&
        ProratedAdjustmentInCents == other.ProratedAdjustmentInCents &&
        ChargeInCents == other.ChargeInCents &&
        PaymentDueInCents == other.PaymentDueInCents &&
        CreditAppliedInCents == other.CreditAppliedInCents;
}
