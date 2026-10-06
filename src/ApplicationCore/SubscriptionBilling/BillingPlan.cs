namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// A subscription plan (a "Product" in Maxio Billing API terms) offered by the configured product family.
/// </summary>
public class BillingPlan
{
    /// <summary>The billing system's numeric id for the plan. Not stable across re-seeded catalogs.</summary>
    public int Id { get; init; }

    /// <summary>The stable API handle used to subscribe (e.g. "eshop-pro").</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in minor units (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Numerical renewal interval, e.g. 1.</summary>
    public int Interval { get; init; } = 1;

    /// <summary>Interval unit, "month" or "day".</summary>
    public string IntervalUnit { get; init; } = "month";

    /// <summary>Whether the plan requires a stored payment method at signup.</summary>
    public bool RequiresPaymentProfile { get; init; }
}
