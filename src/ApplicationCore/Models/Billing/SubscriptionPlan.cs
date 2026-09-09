using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// A subscribable plan as exposed by the billing system of record (Maxio Advanced Billing).
/// </summary>
public sealed class SubscriptionPlan
{
    /// <summary>
    /// The stable API handle of the product backing this plan.
    /// </summary>
    public string Handle { get; init; } = default!;

    public string Name { get; init; } = default!;

    public string? Description { get; init; }

    /// <summary>
    /// Recurring price for the plan, in minor currency units (e.g. cents).
    /// </summary>
    public long PriceInCents { get; init; }

    /// <summary>
    /// Number of <see cref="IntervalUnit"/> between billing periods.
    /// </summary>
    public int Interval { get; init; }

    /// <summary>
    /// Billing interval unit (e.g. "month", "day").
    /// </summary>
    public string IntervalUnit { get; init; } = default!;

    /// <summary>
    /// True when the underlying product requires a payment method at signup.
    /// </summary>
    public bool RequiresPaymentMethod { get; init; }
}
