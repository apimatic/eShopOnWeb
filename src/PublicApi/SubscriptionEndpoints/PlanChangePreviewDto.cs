using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// What a plan change will cost. All money is in whole currency units.
/// </summary>
public class PlanChangePreviewDto
{
    public int SubscriptionId { get; set; }
    public SubscriptionPlanDto CurrentPlan { get; set; }
    public SubscriptionPlanDto TargetPlan { get; set; }

    /// <summary>Either <c>Immediate</c> or <c>AtNextRenewal</c>.</summary>
    public string Timing { get; set; }

    public decimal ProratedCharge { get; set; }
    public decimal ProratedCredit { get; set; }

    /// <summary>Charge minus credit. Positive means the customer is out of pocket; negative is a credit.</summary>
    public decimal NetAmount { get; set; }

    /// <summary>
    /// What is actually billed on confirming. A downgrade usually nets to an account credit rather
    /// than a refund, so this is zero even when <see cref="NetAmount"/> is negative.
    /// </summary>
    public decimal AmountDueNow { get; set; }

    public DateTimeOffset? EffectiveAt { get; set; }

    /// <summary>
    /// Echo this back on the commit call. It proves which figures the customer agreed to, and the
    /// commit is rejected if the cost has moved since.
    /// </summary>
    public string Fingerprint { get; set; }
}
