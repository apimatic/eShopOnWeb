using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The priced result of a proposed plan change. Amounts are in whole currency units.
/// <para>
/// <see cref="Signature"/> must be sent back when committing the change: it proves the customer
/// confirmed these exact amounts, and the commit is refused if re-pricing no longer matches.
/// </para>
/// </summary>
public class PlanChangePreviewDto
{
    public int SubscriptionId { get; set; }
    public string CurrentPlanHandle { get; set; }
    public string CurrentPlanName { get; set; }
    public string TargetPlanHandle { get; set; }
    public string TargetPlanName { get; set; }
    public string Timing { get; set; }
    public decimal ProratedAdjustment { get; set; }
    public decimal Charge { get; set; }
    public decimal PaymentDue { get; set; }
    public decimal CreditApplied { get; set; }
    public decimal TargetPlanPrice { get; set; }
    public DateTimeOffset? EffectiveAt { get; set; }

    /// <summary>Fingerprint of the priced facts. Required when committing the change.</summary>
    public string Signature { get; set; }
}
