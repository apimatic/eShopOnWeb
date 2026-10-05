using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription as recorded in the billing system, confirmed back to the shopper:
/// plan, price, state and next billing date.
/// </summary>
public class SubscriptionDto
{
    /// <summary>
    /// The numeric Maxio subscription id.
    /// </summary>
    public int SubscriptionId { get; set; }

    /// <summary>
    /// The Maxio product (plan) handle.
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;

    public string? ProductName { get; set; }

    /// <summary>
    /// The Maxio subscription state, e.g. "active", "trialing", "canceled".
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// The recurring price of this subscription in cents (may differ from the plan's current
    /// price if the plan price changed after signup).
    /// </summary>
    public long? PriceInCents { get; set; }

    /// <summary>
    /// The recurring price formatted for display, when known.
    /// </summary>
    public string? PriceDisplay { get; set; }

    /// <summary>
    /// When the current billing period ends — the next regularly scheduled billing date.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    /// <summary>
    /// The application-generated Maxio subscription reference.
    /// </summary>
    public string Reference { get; set; } = string.Empty;
}