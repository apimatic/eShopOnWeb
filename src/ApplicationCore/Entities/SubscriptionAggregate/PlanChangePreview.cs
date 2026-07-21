namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The prorated cost/credit preview for an immediate plan change, shown to the customer before
/// they confirm (UC3). Only meaningful for the "apply now, with proration" timing — there is no
/// provider-side preview for the "at next renewal, no proration" timing.
/// </summary>
public class PlanChangePreview
{
    public PlanChangePreview(string targetProductHandle, long proratedAdjustmentInCents, long chargeInCents, long paymentDueInCents, long creditAppliedInCents)
    {
        TargetProductHandle = targetProductHandle;
        ProratedAdjustmentInCents = proratedAdjustmentInCents;
        ChargeInCents = chargeInCents;
        PaymentDueInCents = paymentDueInCents;
        CreditAppliedInCents = creditAppliedInCents;
    }

    public string TargetProductHandle { get; }
    public long ProratedAdjustmentInCents { get; }
    public long ChargeInCents { get; }
    public long PaymentDueInCents { get; }
    public long CreditAppliedInCents { get; }

    public decimal PaymentDue => PaymentDueInCents / 100m;
    public decimal CreditApplied => CreditAppliedInCents / 100m;
}
