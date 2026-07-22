using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// What a plan change will cost, computed before the customer commits (UC3 step 2).
/// All amounts are in minor units (cents), exactly as the provider reports them.
/// </summary>
public class PlanChangePreview
{
    public PlanChangePreview(int subscriptionId,
        string currentPlanHandle,
        string targetPlanHandle,
        PlanChangeTiming timing,
        long proratedAdjustmentInCents,
        long chargeInCents,
        long paymentDueInCents,
        long creditAppliedInCents)
    {
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        SubscriptionId = subscriptionId;
        CurrentPlanHandle = currentPlanHandle;
        TargetPlanHandle = targetPlanHandle;
        Timing = timing;
        ProratedAdjustmentInCents = proratedAdjustmentInCents;
        ChargeInCents = chargeInCents;
        PaymentDueInCents = paymentDueInCents;
        CreditAppliedInCents = creditAppliedInCents;
    }

    public int SubscriptionId { get; }
    public string CurrentPlanHandle { get; }
    public string TargetPlanHandle { get; }
    public PlanChangeTiming Timing { get; }

    /// <summary>The prorated adjustment issued against the current plan.</summary>
    public long ProratedAdjustmentInCents { get; }

    /// <summary>The charge raised for the new plan.</summary>
    public long ChargeInCents { get; }

    /// <summary>What the customer actually pays now (an upgrade); zero for a downgrade.</summary>
    public long PaymentDueInCents { get; }

    /// <summary>Credit applied as part of the change.</summary>
    public long CreditAppliedInCents { get; }

    public decimal ProratedAdjustment => ProratedAdjustmentInCents / 100m;
    public decimal Charge => ChargeInCents / 100m;

    /// <summary>The amount due now, in major units (dollars) — the figure shown to the customer.</summary>
    public decimal PaymentDue => PaymentDueInCents / 100m;

    public decimal CreditApplied => CreditAppliedInCents / 100m;

    /// <summary>
    /// Whether two previews quote the same amounts. Used to reject a commit whose preview went
    /// stale between display and confirmation (UC3 failure scenario).
    /// </summary>
    public bool QuotesSameAmountsAs(PlanChangePreview other) =>
        other is not null
        && TargetPlanHandle == other.TargetPlanHandle
        && Timing == other.Timing
        && ProratedAdjustmentInCents == other.ProratedAdjustmentInCents
        && ChargeInCents == other.ChargeInCents
        && PaymentDueInCents == other.PaymentDueInCents
        && CreditAppliedInCents == other.CreditAppliedInCents;
}
