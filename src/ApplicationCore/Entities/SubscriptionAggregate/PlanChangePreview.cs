using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The cost of moving a subscription to another plan, computed before anything is committed (UC3).
/// All amounts are in dollars.
/// </summary>
public sealed record PlanChangePreview
{
    public PlanChangePreview(int subscriptionId,
        string currentPlanHandle,
        string targetPlanHandle,
        PlanChangeTiming timing)
    {
        if (string.IsNullOrWhiteSpace(currentPlanHandle)) throw new ArgumentException("A current plan handle is required.", nameof(currentPlanHandle));
        if (string.IsNullOrWhiteSpace(targetPlanHandle)) throw new ArgumentException("A target plan handle is required.", nameof(targetPlanHandle));

        SubscriptionId = subscriptionId;
        CurrentPlanHandle = currentPlanHandle;
        TargetPlanHandle = targetPlanHandle;
        Timing = timing;
    }

    public int SubscriptionId { get; init; }

    public string CurrentPlanHandle { get; init; }

    public string TargetPlanHandle { get; init; }

    public PlanChangeTiming Timing { get; init; }

    /// <summary>Net prorated adjustment for the remainder of the current period, in dollars.</summary>
    public decimal ProratedAdjustment { get; init; }

    /// <summary>Charge raised by the change, in dollars.</summary>
    public decimal Charge { get; init; }

    /// <summary>Amount immediately due, in dollars.</summary>
    public decimal PaymentDue { get; init; }

    /// <summary>Credit applied against the change, in dollars.</summary>
    public decimal CreditApplied { get; init; }

    /// <summary>Recurring price of the target plan, in dollars.</summary>
    public decimal TargetPlanPrice { get; init; }

    /// <summary>When the change takes effect. Null for an immediate change.</summary>
    public DateTimeOffset? EffectiveAt { get; init; }

    /// <summary>
    /// False for an at-next-renewal change: the provider prices that path at the next period boundary
    /// and exposes no proration preview for it, so no proration is previewed or charged.
    /// </summary>
    public bool IsProrated => Timing == PlanChangeTiming.Immediately;

    /// <summary>
    /// A deterministic digest of everything the customer was shown. UC3 requires that a commit is
    /// rejected when the basis moved between preview and confirm; the service re-previews at commit
    /// time and compares this value, so a changed price or proration basis can never be applied
    /// silently.
    /// </summary>
    public string Fingerprint => string.Create(CultureInfo.InvariantCulture,
        $"{SubscriptionId}|{CurrentPlanHandle}|{TargetPlanHandle}|{(int)Timing}|{ProratedAdjustment:F2}|{Charge:F2}|{PaymentDue:F2}|{CreditApplied:F2}|{TargetPlanPrice:F2}");
}
