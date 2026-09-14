namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// A subscription plan a shopper can enroll in — one Maxio product in the configured product family.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable plan handle (e.g. <c>eshop-pro</c>) — pass this to the subscribe endpoint.</summary>
    public string Handle { get; set; } = string.Empty;

    public int? ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Human-readable price, e.g. <c>$299.00</c>.</summary>
    public string FormattedPrice { get; set; } = string.Empty;

    /// <summary>Billing interval count (e.g. 1).</summary>
    public int? Interval { get; set; }

    /// <summary>Billing interval unit, e.g. <c>month</c> or <c>day</c>.</summary>
    public string? IntervalUnit { get; set; }
}
