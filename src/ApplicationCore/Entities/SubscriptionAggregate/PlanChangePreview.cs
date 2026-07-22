using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The cost of moving a subscription to another plan, computed by the provider before anything
/// is committed. All amounts are in whole currency units (dollars), never cents.
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
        Guard.Against.NullOrEmpty(currentPlanHandle, nameof(currentPlanHandle));
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

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

    /// <summary>Net prorated adjustment: positive when the customer owes more, negative when credited.</summary>
    public decimal ProratedAdjustment { get; private set; }

    public decimal Charge { get; private set; }

    public decimal PaymentDue { get; private set; }

    public decimal CreditApplied { get; private set; }

    /// <summary>
    /// Fingerprint of the previewed amounts, carried through the confirm step so a preview that no
    /// longer matches the provider's current numbers is rejected instead of silently applied (UC3).
    /// </summary>
    public string Fingerprint =>
        $"{CurrentPlanHandle}|{TargetPlanHandle}|{Timing}|{ProratedAdjustment}|{Charge}|{PaymentDue}|{CreditApplied}";
}
