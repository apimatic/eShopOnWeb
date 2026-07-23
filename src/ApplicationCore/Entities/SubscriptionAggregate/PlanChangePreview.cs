namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The prorated cost of moving a subscription to another plan, computed by the provider without
/// committing anything (UC3, step 2).
/// </summary>
/// <remarks>All amounts are in minor units (cents), exactly as the provider reports them.</remarks>
public class PlanChangePreview
{
    public PlanChangePreview(string currentPlanHandle,
        string targetPlanHandle,
        PlanChangeTiming timing,
        long proratedAdjustmentInCents,
        long chargeInCents,
        long paymentDueInCents,
        long creditAppliedInCents)
    {
        CurrentPlanHandle = currentPlanHandle;
        TargetPlanHandle = targetPlanHandle;
        Timing = timing;
        ProratedAdjustmentInCents = proratedAdjustmentInCents;
        ChargeInCents = chargeInCents;
        PaymentDueInCents = paymentDueInCents;
        CreditAppliedInCents = creditAppliedInCents;
    }

    public string CurrentPlanHandle { get; }

    public string TargetPlanHandle { get; }

    public PlanChangeTiming Timing { get; }

    public long ProratedAdjustmentInCents { get; }

    public long ChargeInCents { get; }

    /// <summary>The amount the customer will actually be charged now, in minor units (cents).</summary>
    public long PaymentDueInCents { get; }

    public long CreditAppliedInCents { get; }

    public decimal ProratedAdjustment => ProratedAdjustmentInCents / 100m;

    public decimal Charge => ChargeInCents / 100m;

    public decimal PaymentDue => PaymentDueInCents / 100m;

    public decimal CreditApplied => CreditAppliedInCents / 100m;

    /// <summary>
    /// True when this preview describes the same commitment as <paramref name="other"/>. Used to
    /// reject a commit whose basis drifted between preview and confirmation (UC3 failure scenario).
    /// </summary>
    public bool MatchesCommitmentOf(PlanChangePreview other)
    {
        if (other is null)
        {
            return false;
        }

        return string.Equals(TargetPlanHandle, other.TargetPlanHandle, System.StringComparison.OrdinalIgnoreCase)
            && Timing == other.Timing
            && ProratedAdjustmentInCents == other.ProratedAdjustmentInCents
            && ChargeInCents == other.ChargeInCents
            && PaymentDueInCents == other.PaymentDueInCents
            && CreditAppliedInCents == other.CreditAppliedInCents;
    }
}
