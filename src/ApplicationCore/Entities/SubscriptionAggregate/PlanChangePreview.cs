namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// What a plan change would cost, quoted by the provider before anything is committed (UC3).
/// All amounts are in minor units (cents), matching how the provider quotes them.
/// </summary>
public class PlanChangePreview
{
    public PlanChangePreview(string targetPlanHandle, PlanChangeTiming timing, long proratedAdjustmentInCents,
        long chargeInCents, long paymentDueInCents, long creditAppliedInCents)
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

    /// <summary>
    /// Credit issued for the unused remainder of the current plan.
    /// </summary>
    public long ProratedAdjustmentInCents { get; }

    /// <summary>
    /// The charge raised for the new plan.
    /// </summary>
    public long ChargeInCents { get; }

    /// <summary>
    /// What the customer actually owes now — the figure shown before they confirm.
    /// </summary>
    public long PaymentDueInCents { get; }

    public long CreditAppliedInCents { get; }

    /// <summary>
    /// <see cref="PaymentDueInCents"/> in major units (dollars).
    /// </summary>
    public decimal PaymentDue => PaymentDueInCents / 100m;

    /// <summary>
    /// True when the quoted amounts still match, so a previewed change is safe to commit (UC3).
    /// </summary>
    public bool Matches(PlanChangePreview other) =>
        other is not null &&
        TargetPlanHandle == other.TargetPlanHandle &&
        Timing == other.Timing &&
        ProratedAdjustmentInCents == other.ProratedAdjustmentInCents &&
        ChargeInCents == other.ChargeInCents &&
        PaymentDueInCents == other.PaymentDueInCents &&
        CreditAppliedInCents == other.CreditAppliedInCents;
}
