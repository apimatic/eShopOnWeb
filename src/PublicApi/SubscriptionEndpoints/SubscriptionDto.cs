using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription the authenticated user holds, sourced from Maxio Advanced Billing.
/// </summary>
public class SubscriptionDto
{
    /// <summary>
    /// Maxio subscription id.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Maxio subscription state (active, trialing, past_due, canceled, ...).
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Stable handle of the subscribed plan (Maxio product handle).
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>
    /// Recurring price per billing period, in cents.
    /// </summary>
    public long PriceInCents { get; set; }

    /// <summary>
    /// Recurring price per billing period, formatted as a decimal currency string (e.g. "299.00").
    /// </summary>
    public string Price { get; set; } = string.Empty;

    /// <summary>
    /// When the next billing cycle renews (ISO 8601), i.e. the next billing date.
    /// </summary>
    public string? NextBillingDate { get; set; }

    public string? ActivatedAt { get; set; }
}
