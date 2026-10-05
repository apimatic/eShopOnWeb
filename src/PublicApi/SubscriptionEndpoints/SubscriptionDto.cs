using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The subscription created (or already held) for the user.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }

    /// <summary>
    /// Maxio subscription state - "active", "trialing", "past_due", "canceled", ...
    /// </summary>
    public string State { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>
    /// Recurring price per billing interval.
    /// </summary>
    public decimal Price { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Next date the subscription will be billed (null for ended subscriptions).
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; set; }
}