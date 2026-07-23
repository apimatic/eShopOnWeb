namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The cost of a plan change, computed by the provider before anything is committed (UC3 step 2).
/// All amounts are in minor units (cents) and follow the provider's sign convention: credits are
/// negative, charges are positive.
/// </summary>
public sealed class PlanChangePreview
{
    public PlanChangePreview(string targetProductHandle,
        PlanChangeTiming timing,
        int proratedAdjustmentInCents,
        int chargeInCents,
        int paymentDueInCents,
        int creditAppliedInCents)
    {
        TargetProductHandle = targetProductHandle;
        Timing = timing;
        ProratedAdjustmentInCents = proratedAdjustmentInCents;
        ChargeInCents = chargeInCents;
        PaymentDueInCents = paymentDueInCents;
        CreditAppliedInCents = creditAppliedInCents;
    }

    public string TargetProductHandle { get; }

    public PlanChangeTiming Timing { get; }

    /// <summary>Credit for the unused remainder of the current plan (negative) or the extra charge for it.</summary>
    public int ProratedAdjustmentInCents { get; }

    /// <summary>Charge for the new plan over the remainder of the period.</summary>
    public int ChargeInCents { get; }

    /// <summary>What the customer actually owes now, after credit is applied.</summary>
    public int PaymentDueInCents { get; }

    /// <summary>Credit consumed by this change.</summary>
    public int CreditAppliedInCents { get; }

    /// <summary>What the customer owes now, in major units — the figure shown for confirmation.</summary>
    public decimal PaymentDue => PaymentDueInCents / 100m;

    public decimal ProratedAdjustment => ProratedAdjustmentInCents / 100m;

    public decimal Charge => ChargeInCents / 100m;

    public decimal CreditApplied => CreditAppliedInCents / 100m;

    /// <summary>
    /// A compact fingerprint of the previewed amounts. UC3 requires the commit to be rejected when
    /// the provider's numbers moved between preview and confirm, rather than silently charging a
    /// different amount than the one shown.
    /// </summary>
    public string Fingerprint =>
        $"{TargetProductHandle}|{Timing}|{ProratedAdjustmentInCents}|{ChargeInCents}|{PaymentDueInCents}|{CreditAppliedInCents}";
}
