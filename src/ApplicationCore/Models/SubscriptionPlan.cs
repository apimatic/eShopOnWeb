using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A subscribable plan as exposed by the billing system of record (Maxio Advanced Billing).
/// </summary>
public class SubscriptionPlan
{
    /// <summary>
    /// The billing system's numeric product id. May be reassigned on re-seed of the billing site;
    /// prefer <see cref="Handle"/> for stable references.
    /// </summary>
    public int ProductId { get; set; }

    /// <summary>
    /// The stable API handle of the plan (e.g. "eshop-pro").
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Recurring price per billing period in major currency units (e.g. 299.00).
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Number of <see cref="IntervalUnit"/>s between billings.
    /// </summary>
    public int Interval { get; set; }

    /// <summary>
    /// Billing interval unit, e.g. "month" or "day".
    /// </summary>
    public string IntervalUnit { get; set; } = "month";

    /// <summary>
    /// True when the plan cannot be enrolled in without a payment method on file.
    /// </summary>
    public bool RequiresPaymentMethod { get; set; }

    public string ProductFamilyHandle { get; set; } = string.Empty;
}
