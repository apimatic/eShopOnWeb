namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The cost of moving a subscription to another plan, shown to the customer before they commit (UC3).
/// All amounts are in whole currency units (e.g. -299.00), not minor units.
/// </summary>
public class PlanChangePreview
{
    public PlanChangePreview(string currentPlanHandle,
        string targetPlanHandle,
        PlanChangeTiming timing,
        decimal proratedAdjustment,
        decimal charge,
        decimal paymentDue,
        decimal creditApplied)
    {
        CurrentPlanHandle = currentPlanHandle;
        TargetPlanHandle = targetPlanHandle;
        Timing = timing;
        ProratedAdjustment = proratedAdjustment;
        Charge = charge;
        PaymentDue = paymentDue;
        CreditApplied = creditApplied;
    }

    public string CurrentPlanHandle { get; private set; }
    public string TargetPlanHandle { get; private set; }
    public PlanChangeTiming Timing { get; private set; }

    /// <summary>Credit issued for the unused remainder of the current plan (negative when refunded).</summary>
    public decimal ProratedAdjustment { get; private set; }

    /// <summary>Charge created for the new plan.</summary>
    public decimal Charge { get; private set; }

    /// <summary>Amount actually due now, typically on an upgrade.</summary>
    public decimal PaymentDue { get; private set; }

    public decimal CreditApplied { get; private set; }

    /// <summary>The net effect on the customer: positive means they pay, negative means they are credited.</summary>
    public decimal NetAmount => Charge + ProratedAdjustment;
}
