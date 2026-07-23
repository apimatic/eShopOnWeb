using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// What a plan change would cost if it were committed now. All provider amounts are in minor units;
/// the major-unit accessors are what the storefront shows the customer before they confirm.
/// </summary>
public class PlanChangePreview
{
    public PlanChangePreview(SubscriptionPlan currentPlan,
        SubscriptionPlan targetPlan,
        PlanChangeTiming timing,
        int proratedAdjustmentInCents,
        int chargeInCents,
        int paymentDueInCents,
        int creditAppliedInCents)
    {
        Guard.Against.Null(currentPlan, nameof(currentPlan));
        Guard.Against.Null(targetPlan, nameof(targetPlan));

        CurrentPlan = currentPlan;
        TargetPlan = targetPlan;
        Timing = timing;
        ProratedAdjustmentInCents = proratedAdjustmentInCents;
        ChargeInCents = chargeInCents;
        PaymentDueInCents = paymentDueInCents;
        CreditAppliedInCents = creditAppliedInCents;
    }

    public SubscriptionPlan CurrentPlan { get; }
    public SubscriptionPlan TargetPlan { get; }
    public PlanChangeTiming Timing { get; }

    /// <summary>The prorated adjustment issued against the current plan, in minor units.</summary>
    public int ProratedAdjustmentInCents { get; }

    /// <summary>The charge raised for the new plan, in minor units.</summary>
    public int ChargeInCents { get; }

    /// <summary>What the customer actually owes now, in minor units.</summary>
    public int PaymentDueInCents { get; }

    /// <summary>The credit applied against the change, in minor units.</summary>
    public int CreditAppliedInCents { get; }

    public decimal ProratedAdjustment => ProratedAdjustmentInCents / 100m;
    public decimal Charge => ChargeInCents / 100m;
    public decimal PaymentDue => PaymentDueInCents / 100m;
    public decimal CreditApplied => CreditAppliedInCents / 100m;
}
