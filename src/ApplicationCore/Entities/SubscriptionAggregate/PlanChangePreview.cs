using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// What a plan change would cost, computed by the provider before anything is committed (UC3 step 2).
/// All money is in whole currency units.
/// </summary>
public class PlanChangePreview
{
    public PlanChangePreview(int subscriptionId,
        string? currentPlanHandle,
        string? currentPlanName,
        decimal currentPlanPrice,
        string targetPlanHandle,
        string targetPlanName,
        decimal targetPlanPrice,
        PlanChangeTiming timing,
        decimal proratedAdjustment,
        decimal proratedCharge,
        decimal creditApplied,
        decimal amountDue,
        DateTimeOffset? effectiveAt)
    {
        SubscriptionId = subscriptionId;
        CurrentPlanHandle = currentPlanHandle;
        CurrentPlanName = currentPlanName;
        CurrentPlanPrice = currentPlanPrice;
        TargetPlanHandle = targetPlanHandle;
        TargetPlanName = targetPlanName;
        TargetPlanPrice = targetPlanPrice;
        Timing = timing;
        ProratedAdjustment = proratedAdjustment;
        ProratedCharge = proratedCharge;
        CreditApplied = creditApplied;
        AmountDue = amountDue;
        EffectiveAt = effectiveAt;
    }

    public int SubscriptionId { get; }

    public string? CurrentPlanHandle { get; }

    public string? CurrentPlanName { get; }

    public decimal CurrentPlanPrice { get; }

    public string TargetPlanHandle { get; }

    public string TargetPlanName { get; }

    public decimal TargetPlanPrice { get; }

    public PlanChangeTiming Timing { get; }

    /// <summary>Credit for the unused portion of the current plan.</summary>
    public decimal ProratedAdjustment { get; }

    /// <summary>Prorated charge for the remainder of the period on the target plan.</summary>
    public decimal ProratedCharge { get; }

    public decimal CreditApplied { get; }

    /// <summary>Net amount payable now. Always zero when <see cref="Timing"/> is at next renewal.</summary>
    public decimal AmountDue { get; }

    /// <summary>When the change takes effect: now, or the end of the current period.</summary>
    public DateTimeOffset? EffectiveAt { get; }
}
