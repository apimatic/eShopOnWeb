namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A provider-agnostic preview of a plan change (UC3). All amounts are in whole currency units
/// (dollars). For an immediate change the amounts reflect proration; for an at-renewal change
/// there is no proration and <see cref="ChargeAmount"/> reflects the new plan's recurring price
/// effective from the next period.
/// </summary>
public class PlanChangePreview
{
    public PlanChangePreview(string targetProductHandle, bool applyImmediately,
        decimal proratedAdjustment, decimal chargeAmount, decimal paymentDue, decimal creditApplied)
    {
        TargetProductHandle = targetProductHandle;
        ApplyImmediately = applyImmediately;
        ProratedAdjustment = proratedAdjustment;
        ChargeAmount = chargeAmount;
        PaymentDue = paymentDue;
        CreditApplied = creditApplied;
    }

    public string TargetProductHandle { get; }

    /// <summary>True = apply now with proration; false = apply at next renewal without proration.</summary>
    public bool ApplyImmediately { get; }

    public decimal ProratedAdjustment { get; }

    public decimal ChargeAmount { get; }

    public decimal PaymentDue { get; }

    public decimal CreditApplied { get; }

    /// <summary>
    /// A stable signature of the previewed amounts, used to reject a commit whose preview has gone
    /// stale between preview and confirm (§UC3 failure scenario).
    /// </summary>
    public string Signature =>
        $"{TargetProductHandle}|{ApplyImmediately}|{ProratedAdjustment}|{ChargeAmount}|{PaymentDue}|{CreditApplied}";
}
