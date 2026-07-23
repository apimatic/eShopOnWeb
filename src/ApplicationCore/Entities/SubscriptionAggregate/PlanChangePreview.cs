using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The cost of moving a subscription to another plan, as quoted by the billing provider before
/// the change is committed. All amounts are in whole currency units (dollars).
/// </summary>
public class PlanChangePreview
{
    public PlanChangePreview(string targetPlanHandle,
        decimal proratedAdjustment,
        decimal charge,
        decimal paymentDue,
        decimal creditApplied)
    {
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        TargetPlanHandle = targetPlanHandle;
        ProratedAdjustment = proratedAdjustment;
        Charge = charge;
        PaymentDue = paymentDue;
        CreditApplied = creditApplied;
    }

    public string TargetPlanHandle { get; }

    /// <summary>Credit for the unused remainder of the current plan. Negative when credit is due.</summary>
    public decimal ProratedAdjustment { get; }

    /// <summary>The charge for the new plan over the remainder of the period.</summary>
    public decimal Charge { get; }

    /// <summary>What the customer actually pays now, after credit is applied.</summary>
    public decimal PaymentDue { get; }

    /// <summary>Credit applied against the charge. Negative when credit is consumed.</summary>
    public decimal CreditApplied { get; }
}
