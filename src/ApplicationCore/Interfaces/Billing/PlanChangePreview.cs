using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

public class PlanChangePreview
{
    public PlanChangePreview(
        bool applyNow,
        long? proratedAdjustmentInCents,
        long? chargeInCents,
        long? paymentDueInCents,
        long? creditAppliedInCents,
        long targetPriceInCents,
        DateTimeOffset? effectiveAt,
        string? note)
    {
        ApplyNow = applyNow;
        ProratedAdjustmentInCents = proratedAdjustmentInCents;
        ChargeInCents = chargeInCents;
        PaymentDueInCents = paymentDueInCents;
        CreditAppliedInCents = creditAppliedInCents;
        TargetPriceInCents = targetPriceInCents;
        EffectiveAt = effectiveAt;
        Note = note;
    }

    /// <summary>True = apply now with proration; false = apply at next renewal, no proration.</summary>
    public bool ApplyNow { get; }
    public long? ProratedAdjustmentInCents { get; }
    public long? ChargeInCents { get; }
    public long? PaymentDueInCents { get; }
    public long? CreditAppliedInCents { get; }
    public long TargetPriceInCents { get; }
    public decimal TargetPrice => TargetPriceInCents / 100m;
    public DateTimeOffset? EffectiveAt { get; }

    /// <summary>Explanatory note — e.g. no-proration-preview-available for the at-renewal path.</summary>
    public string? Note { get; }
}
