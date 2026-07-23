namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The cost of moving a subscription to another plan, computed before the customer commits (UC3).
/// </summary>
/// <param name="SubscriptionId">The subscription the preview was computed for.</param>
/// <param name="CurrentPlanHandle">The plan the subscription is on today.</param>
/// <param name="TargetPlanHandle">The plan the subscription would move to.</param>
/// <param name="Timing">Whether the preview assumes an immediate (prorated) or deferred change.</param>
/// <param name="ProratedAdjustmentInCents">The prorated adjustment for the remainder of the period, in cents.</param>
/// <param name="ChargeInCents">The gross charge the change would raise, in cents.</param>
/// <param name="CreditAppliedInCents">Credit applied against that charge, in cents.</param>
/// <param name="PaymentDueInCents">
/// What the customer actually pays now, in cents. This is the figure shown to the customer and
/// the figure the commit is checked against, so a stale preview can never be applied silently.
/// </param>
public record PlanChangePreview(
    int SubscriptionId,
    string CurrentPlanHandle,
    string TargetPlanHandle,
    PlanChangeTiming Timing,
    long ProratedAdjustmentInCents,
    long ChargeInCents,
    long CreditAppliedInCents,
    long PaymentDueInCents)
{
    /// <summary>The prorated adjustment in the site's currency unit (dollars).</summary>
    public decimal ProratedAdjustment => ProratedAdjustmentInCents / 100m;

    /// <summary>The gross charge in the site's currency unit (dollars).</summary>
    public decimal Charge => ChargeInCents / 100m;

    /// <summary>The credit applied in the site's currency unit (dollars).</summary>
    public decimal CreditApplied => CreditAppliedInCents / 100m;

    /// <summary>What the customer pays now, in the site's currency unit (dollars).</summary>
    public decimal PaymentDue => PaymentDueInCents / 100m;
}
