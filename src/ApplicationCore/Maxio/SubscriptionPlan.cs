namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A purchasable plan. In Maxio Advanced Billing's classic catalog, plans are
/// modeled as products belonging to a product family.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Numeric id of the plan in the billing system.</summary>
    public long Id { get; set; }

    /// <summary>Stable, site-unique handle of the plan (e.g. "eshop-pro").</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>List price in integer cents.</summary>
    public int PriceInCents { get; set; }

    /// <summary>Renewal interval magnitude, e.g. 1 for "monthly".</summary>
    public int? Interval { get; set; }

    /// <summary>Renewal interval unit: "month" or "day".</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>True when the plan cannot be subscribed to without a payment method.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public bool Taxable { get; set; }
}
