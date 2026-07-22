namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The cost of moving a subscription to another plan, shown to the customer before they commit (UC3).
/// All amounts are in major currency units (e.g. 24.95 — not cents).
/// </summary>
public class PlanChangePreview
{
    public string CurrentPlanHandle { get; init; } = string.Empty;
    public string TargetPlanHandle { get; init; } = string.Empty;

    /// <summary>True to prorate and apply now; false to apply at the next renewal.</summary>
    public bool ApplyImmediately { get; init; }

    /// <summary>The prorated adjustment issued for the current plan.</summary>
    public decimal ProratedAdjustment { get; init; }

    /// <summary>The charge that would be created for the new plan.</summary>
    public decimal Charge { get; init; }

    /// <summary>The amount payable now — the value the customer confirms.</summary>
    public decimal PaymentDue { get; init; }

    /// <summary>The credit applied against the change.</summary>
    public decimal CreditApplied { get; init; }
}
