using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription held by the caller, as recorded in the billing system of record.
/// </summary>
public class SubscriptionItemDto
{
    /// <summary>
    /// Billing system (Maxio) subscription id.
    /// </summary>
    public int SubscriptionId { get; set; }

    /// <summary>
    /// Lifecycle state (e.g. "active", "past_due", "canceled").
    /// </summary>
    public string State { get; set; } = default!;

    /// <summary>
    /// Handle of the subscribed plan.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Display name of the subscribed plan.
    /// </summary>
    public string? PlanName { get; set; }

    /// <summary>
    /// Recurring price in minor currency units (e.g. cents).
    /// </summary>
    public long PriceInCents { get; set; }

    /// <summary>
    /// Next regularly scheduled charge date (UTC), when applicable.
    /// </summary>
    public DateTime? NextBillingDateUtc { get; set; }

    /// <summary>
    /// When the subscription was created (UTC).
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }
}
