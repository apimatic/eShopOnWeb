namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The prorated cost of a plan change, quoted before anything is committed. Each amount is given in
/// major currency units and, alongside it, in the minor units the billing provider itself reports —
/// so a caller can confirm the change with the exact integer amount it was quoted, with no rounding
/// in between.
/// </summary>
public class PlanChangePreviewDto
{
    public string CurrentPlanHandle { get; set; } = string.Empty;
    public string TargetPlanHandle { get; set; } = string.Empty;

    /// <summary><c>Immediate</c> or <c>NextRenewal</c>.</summary>
    public string Timing { get; set; } = string.Empty;

    public decimal ProratedAdjustment { get; set; }
    public decimal Charge { get; set; }

    /// <summary>What the customer is charged now.</summary>
    public decimal PaymentDue { get; set; }

    public decimal CreditApplied { get; set; }
    public decimal TargetPlanPrice { get; set; }

    public long ProratedAdjustmentInCents { get; set; }
    public long ChargeInCents { get; set; }

    /// <summary><see cref="PaymentDue"/> in minor units. Echo this back to confirm the change.</summary>
    public long AmountDueInCents { get; set; }

    /// <summary>The same figure as <see cref="AmountDueInCents"/>, under the payment-due name.</summary>
    public long PaymentDueInCents { get; set; }

    public long CreditAppliedInCents { get; set; }
    public long TargetPlanPriceInCents { get; set; }
}
