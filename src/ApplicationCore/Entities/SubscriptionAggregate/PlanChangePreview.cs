using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A priced-out preview of a UC3 plan change, shown to the customer before they confirm. For
/// <see cref="PlanChangeTiming.AtNextRenewal"/> there is no proration (documented provider behavior),
/// so the prorated fields are null and only <see cref="NewPlanPriceInCents"/>/<see cref="EffectiveAt"/> apply.
/// </summary>
public class PlanChangePreview
{
    public PlanChangePreview(
        int subscriptionId,
        string currentProductHandle,
        string targetProductHandle,
        PlanChangeTiming timing,
        long? proratedAdjustmentInCents,
        long? chargeInCents,
        long? paymentDueInCents,
        long? creditAppliedInCents,
        long newPlanPriceInCents,
        DateTimeOffset? effectiveAt)
    {
        SubscriptionId = subscriptionId;
        CurrentProductHandle = currentProductHandle;
        TargetProductHandle = targetProductHandle;
        Timing = timing;
        ProratedAdjustmentInCents = proratedAdjustmentInCents;
        ChargeInCents = chargeInCents;
        PaymentDueInCents = paymentDueInCents;
        CreditAppliedInCents = creditAppliedInCents;
        NewPlanPriceInCents = newPlanPriceInCents;
        EffectiveAt = effectiveAt;
    }

    public int SubscriptionId { get; }
    public string CurrentProductHandle { get; }
    public string TargetProductHandle { get; }
    public PlanChangeTiming Timing { get; }
    public long? ProratedAdjustmentInCents { get; }
    public long? ChargeInCents { get; }
    public long? PaymentDueInCents { get; }
    public long? CreditAppliedInCents { get; }
    public long NewPlanPriceInCents { get; }
    public DateTimeOffset? EffectiveAt { get; }

    /// <summary>
    /// The basis a stale-preview check (UC3) must compare at commit time must match exactly (plan
    /// handle, timing, and the flat new-plan price never drift between two calls). The prorated
    /// dollar figures are a continuous function of elapsed time that Maxio rounds to the nearest cent
    /// on every call — verified live: two previews issued milliseconds apart can each round to either
    /// side of a cent boundary, so comparing them for exact equality rejects most commits on pure
    /// rounding jitter, not a meaningful pricing change. A small tolerance absorbs that jitter while
    /// still catching a genuinely stale preview (a real price/coupon change, or a long delay).
    /// </summary>
    private const long ProrationToleranceInCents = 50;

    public bool HasSamePricingAs(PlanChangePreview other) =>
        TargetProductHandle == other.TargetProductHandle &&
        Timing == other.Timing &&
        NewPlanPriceInCents == other.NewPlanPriceInCents &&
        IsWithinTolerance(ProratedAdjustmentInCents, other.ProratedAdjustmentInCents) &&
        IsWithinTolerance(ChargeInCents, other.ChargeInCents) &&
        IsWithinTolerance(PaymentDueInCents, other.PaymentDueInCents) &&
        IsWithinTolerance(CreditAppliedInCents, other.CreditAppliedInCents);

    private static bool IsWithinTolerance(long? a, long? b)
    {
        if (a is null || b is null)
        {
            return a == b;
        }

        return Math.Abs(a.Value - b.Value) <= ProrationToleranceInCents;
    }
}
