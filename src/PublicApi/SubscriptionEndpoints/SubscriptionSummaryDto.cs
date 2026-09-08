using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription as recorded in the billing system.
/// </summary>
public class SubscriptionSummaryDto
{
    public int BillingSubscriptionId { get; set; }

    public string State { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>
    /// Recurring price, in cents.
    /// </summary>
    public long PriceInCents { get; set; }

    /// <summary>
    /// When the next regularly scheduled billing occurs.
    /// </summary>
    public DateTime? NextBillingDate { get; set; }

    public DateTime? ActivatedAt { get; set; }

    public DateTime? CanceledAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
