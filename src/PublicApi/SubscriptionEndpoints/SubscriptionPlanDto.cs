namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan as returned by the PublicApi.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>The billing system's product id.</summary>
    public int Id { get; set; }

    /// <summary>The billing system's stable handle; pass this as planHandle when subscribing.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in major currency units.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in minor currency units (e.g. cents).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Billing interval count (e.g. 1 = every interval-unit).</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit, e.g. "month".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>True when the plan demands a payment method at signup.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public string ProductFamilyHandle { get; set; } = string.Empty;
}
