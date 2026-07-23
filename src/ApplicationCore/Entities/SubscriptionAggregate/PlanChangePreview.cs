using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// What a plan change would cost, computed by the provider before anything is committed.
/// All amounts are in major units (e.g. 270.00 dollars).
/// </summary>
public class PlanChangePreview
{
    public PlanChangePreview(string targetPlanHandle, decimal proratedAdjustment, decimal charge,
        decimal paymentDue, decimal creditApplied)
    {
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        TargetPlanHandle = targetPlanHandle;
        ProratedAdjustment = proratedAdjustment;
        Charge = charge;
        PaymentDue = paymentDue;
        CreditApplied = creditApplied;
    }

    public string TargetPlanHandle { get; private set; }

    /// <summary>The prorated adjustment issued against the plan being left.</summary>
    public decimal ProratedAdjustment { get; private set; }

    /// <summary>The charge raised for the plan being moved to.</summary>
    public decimal Charge { get; private set; }

    /// <summary>What the customer actually pays now — the amount shown before they confirm.</summary>
    public decimal PaymentDue { get; private set; }

    /// <summary>Credit applied to the subscription as part of the change.</summary>
    public decimal CreditApplied { get; private set; }
}
