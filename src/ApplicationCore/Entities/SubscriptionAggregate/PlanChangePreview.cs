using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>A previewed plan-change cost, shown to the customer before they confirm the change.</summary>
public class PlanChangePreview
{
    public PlanChangePreview(
        string fromPlanHandle,
        string toPlanHandle,
        bool applyNow,
        decimal proratedAmount,
        decimal paymentDueAmount,
        decimal creditAppliedAmount,
        DateTimeOffset effectiveDate)
    {
        FromPlanHandle = fromPlanHandle;
        ToPlanHandle = toPlanHandle;
        ApplyNow = applyNow;
        ProratedAmount = proratedAmount;
        PaymentDueAmount = paymentDueAmount;
        CreditAppliedAmount = creditAppliedAmount;
        EffectiveDate = effectiveDate;
    }

    public string FromPlanHandle { get; }
    public string ToPlanHandle { get; }

    /// <summary>True = prorated change effective now; false = full-price change effective at next renewal.</summary>
    public bool ApplyNow { get; }
    public decimal ProratedAmount { get; }
    public decimal PaymentDueAmount { get; }
    public decimal CreditAppliedAmount { get; }
    public DateTimeOffset EffectiveDate { get; }
}
