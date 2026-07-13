using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A cost preview for moving a subscription to a different plan, computed by the billing
/// provider. Must be re-verified at commit time so the applied amount can never silently
/// diverge from what the customer was shown (UC3). // ValueObject
/// </summary>
public class PlanChangePreview
{
    public PlanChangePreview(
        string currentProductHandle,
        string targetProductHandle,
        bool applyImmediately,
        decimal proratedAdjustmentInCents,
        decimal chargeInCents,
        decimal paymentDueInCents,
        decimal creditAppliedInCents)
    {
        Guard.Against.NullOrEmpty(currentProductHandle, nameof(currentProductHandle));
        Guard.Against.NullOrEmpty(targetProductHandle, nameof(targetProductHandle));

        CurrentProductHandle = currentProductHandle;
        TargetProductHandle = targetProductHandle;
        ApplyImmediately = applyImmediately;
        ProratedAdjustmentInCents = proratedAdjustmentInCents;
        ChargeInCents = chargeInCents;
        PaymentDueInCents = paymentDueInCents;
        CreditAppliedInCents = creditAppliedInCents;
    }

    public string CurrentProductHandle { get; private set; }
    public string TargetProductHandle { get; private set; }

    /// <summary>True: apply now with proration. False: apply at next renewal, no proration.</summary>
    public bool ApplyImmediately { get; private set; }
    public decimal ProratedAdjustmentInCents { get; private set; }
    public decimal ChargeInCents { get; private set; }
    public decimal PaymentDueInCents { get; private set; }
    public decimal CreditAppliedInCents { get; private set; }
}
