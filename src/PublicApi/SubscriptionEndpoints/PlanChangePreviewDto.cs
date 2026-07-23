using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The prorated cost of moving a subscription to another plan.
/// </summary>
/// <remarks>
/// Echo <see cref="PaymentDueInCents"/> and <see cref="PreviewedAt"/> back on the commit call: the change
/// is refused if the amount has moved since the preview was produced.
/// </remarks>
public class PlanChangePreviewDto
{
    public int SubscriptionId { get; set; }

    public string? CurrentPlanHandle { get; set; }

    public string TargetPlanHandle { get; set; } = string.Empty;

    public string? TargetPlanName { get; set; }

    /// <summary>Prorated charge in dollars.</summary>
    public decimal Charge { get; set; }

    /// <summary>Prorated credit in dollars.</summary>
    public decimal CreditApplied { get; set; }

    /// <summary>Net amount due immediately, in dollars.</summary>
    public decimal PaymentDue { get; set; }

    /// <summary>Net amount due immediately, in cents — the value to confirm on commit.</summary>
    public long PaymentDueInCents { get; set; }

    /// <summary>Net proration adjustment in dollars.</summary>
    public decimal ProratedAdjustment { get; set; }

    /// <summary>When the preview was produced — the value to confirm on commit.</summary>
    public DateTimeOffset PreviewedAt { get; set; }
}
