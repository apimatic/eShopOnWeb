using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A user's enrollment in a plan, as recorded by the billing system of record.
/// </summary>
public class UserSubscription
{
    /// <summary>
    /// The billing system's subscription id.
    /// </summary>
    public int SubscriptionId { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>
    /// Recurring price per billing period in major currency units.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Billing system state, e.g. "active", "trialing", "canceled", "past_due".
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// When the current period ends / the next billing will occur.
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// True when the subscription grants entitlement (live, non end-of-life states).
    /// </summary>
    public bool IsActive { get; set; }
}
