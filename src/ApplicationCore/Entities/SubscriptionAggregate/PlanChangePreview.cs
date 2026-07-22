namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The cost of a plan change as quoted by the billing provider before it is committed.
/// All amounts are in minor units (cents), exactly as the provider quotes them, so that the
/// previewed amount and the committed amount can be compared without rounding drift.
/// </summary>
public class PlanChangePreview
{
    public PlanChangePreview(string targetPlanHandle, PlanChangeTiming timing,
        int proratedAdjustmentInCents, int chargeInCents, int paymentDueInCents, int creditAppliedInCents)
    {
        TargetPlanHandle = targetPlanHandle;
        Timing = timing;
        ProratedAdjustmentInCents = proratedAdjustmentInCents;
        ChargeInCents = chargeInCents;
        PaymentDueInCents = paymentDueInCents;
        CreditAppliedInCents = creditAppliedInCents;
    }

    public string TargetPlanHandle { get; }

    public PlanChangeTiming Timing { get; }

    public int ProratedAdjustmentInCents { get; }

    public int ChargeInCents { get; }

    public int PaymentDueInCents { get; }

    public int CreditAppliedInCents { get; }

    public decimal ProratedAdjustment => ProratedAdjustmentInCents / 100m;

    public decimal Charge => ChargeInCents / 100m;

    public decimal PaymentDue => PaymentDueInCents / 100m;

    public decimal CreditApplied => CreditAppliedInCents / 100m;

    /// <summary>
    /// True when this preview quotes the same target plan, timing and amounts as
    /// <paramref name="other"/>. UC3 requires a commit to be rejected when the customer's
    /// confirmed preview no longer matches a freshly taken one.
    /// </summary>
    public bool Matches(PlanChangePreview other)
    {
        if (other is null) return false;

        return TargetPlanHandle == other.TargetPlanHandle
            && Timing == other.Timing
            && ProratedAdjustmentInCents == other.ProratedAdjustmentInCents
            && ChargeInCents == other.ChargeInCents
            && PaymentDueInCents == other.PaymentDueInCents
            && CreditAppliedInCents == other.CreditAppliedInCents;
    }
}
