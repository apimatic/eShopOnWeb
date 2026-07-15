namespace Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;

public class PlanChangePreview
{
    public PlanChangePreview(string targetPlanHandle, long proratedAdjustmentInCents,
        long chargeInCents, long paymentDueInCents, long creditAppliedInCents)
    {
        TargetPlanHandle = targetPlanHandle;
        ProratedAdjustmentInCents = proratedAdjustmentInCents;
        ChargeInCents = chargeInCents;
        PaymentDueInCents = paymentDueInCents;
        CreditAppliedInCents = creditAppliedInCents;
    }

    public string TargetPlanHandle { get; }
    public long ProratedAdjustmentInCents { get; }
    public long ChargeInCents { get; }
    public long PaymentDueInCents { get; }
    public long CreditAppliedInCents { get; }
}
