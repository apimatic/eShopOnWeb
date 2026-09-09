using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// A subscription held by a shopper in the billing system of record (Maxio Advanced Billing).
/// </summary>
public sealed class SubscriptionDetails
{
    /// <summary>
    /// The billing system's unique id for the subscription.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Lifecycle state as reported by the billing system (e.g. "active", "past_due", "canceled").
    /// </summary>
    public string State { get; init; } = default!;

    /// <summary>
    /// API handle of the product backing the subscription, when known.
    /// </summary>
    public string? PlanHandle { get; init; }

    /// <summary>
    /// Display name of the subscribed plan, when known.
    /// </summary>
    public string? PlanName { get; init; }

    /// <summary>
    /// Recurring amount currently charged for the subscription, in minor currency units.
    /// </summary>
    public long PriceInCents { get; init; }

    /// <summary>
    /// When the next regularly scheduled charge will occur (UTC), when applicable.
    /// </summary>
    public DateTime? NextBillingDateUtc { get; init; }

    /// <summary>
    /// When the subscription became active (UTC), when applicable.
    /// </summary>
    public DateTime? ActivatedAtUtc { get; init; }

    /// <summary>
    /// When the subscription was created (UTC).
    /// </summary>
    public DateTime CreatedAtUtc { get; init; }
}
