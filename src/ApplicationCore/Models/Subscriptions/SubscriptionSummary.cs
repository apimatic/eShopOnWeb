using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// A user's subscription as recorded by the billing system of record.
/// </summary>
public class SubscriptionSummary
{
    /// <summary>
    /// The subscription's id in the billing system.
    /// </summary>
    public int BillingSubscriptionId { get; set; }

    /// <summary>
    /// Current lifecycle state as reported by the billing system, e.g. "active", "canceled", "trialing".
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the plan the subscription is for.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>
    /// Recurring price currently billed for this subscription, in cents.
    /// </summary>
    public long PriceInCents { get; set; }

    /// <summary>
    /// When the next regularly scheduled billing occurs (end of the current period).
    /// </summary>
    public DateTime? NextBillingDate { get; set; }

    public DateTime? ActivatedAt { get; set; }

    public DateTime? CanceledAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
