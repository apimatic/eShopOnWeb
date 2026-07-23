using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The cost of moving a subscription to another plan, shown to the customer before they commit
/// (plan.md UC3). Amounts are in dollars. A change deferred to the next renewal prorates nothing, so its
/// charge and credit are both zero and <see cref="NewPlanPrice"/> carries what the customer will pay from
/// <see cref="EffectiveAt"/> onwards.
/// </summary>
public class PlanChangePreview
{
    public PlanChangePreview(int subscriptionId, string currentPlanHandle, string targetPlanHandle,
        PlanChangeTiming timing, decimal prorationCharge, decimal prorationCredit, decimal newPlanPrice,
        DateTimeOffset? effectiveAt)
    {
        SubscriptionId = subscriptionId;
        CurrentPlanHandle = currentPlanHandle;
        TargetPlanHandle = targetPlanHandle;
        Timing = timing;
        ProrationCharge = prorationCharge;
        ProrationCredit = prorationCredit;
        NewPlanPrice = newPlanPrice;
        EffectiveAt = effectiveAt;
    }

    public int SubscriptionId { get; }

    public string CurrentPlanHandle { get; }

    public string TargetPlanHandle { get; }

    public PlanChangeTiming Timing { get; }

    /// <summary>Prorated amount charged for the new plan over the remainder of the period, in dollars.</summary>
    public decimal ProrationCharge { get; }

    /// <summary>Prorated credit for the unused remainder of the old plan, in dollars.</summary>
    public decimal ProrationCredit { get; }

    /// <summary>The recurring price of the target plan, in dollars.</summary>
    public decimal NewPlanPrice { get; }

    /// <summary>When the new plan takes effect; null when the provider did not report a date.</summary>
    public DateTimeOffset? EffectiveAt { get; }

    /// <summary>Net amount the customer will be charged (positive) or credited (negative), in dollars.</summary>
    public decimal NetAmount => ProrationCharge - ProrationCredit;
}
