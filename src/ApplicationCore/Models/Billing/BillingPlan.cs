namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// A recurring plan a customer can subscribe to, normalized from the billing provider.
/// </summary>
public class BillingPlan
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// The recurring price expressed in the site currency (e.g. 299.00), not in minor units.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// The recurring price in minor units (cents), exactly as the provider reports it.
    /// </summary>
    public long PriceInCents { get; set; }

    public string Currency { get; set; } = "USD";

    /// <summary>
    /// The numeric part of the billing interval, e.g. 1 in "every 1 month".
    /// </summary>
    public int Interval { get; set; }

    /// <summary>
    /// The unit part of the billing interval, e.g. "month" in "every 1 month".
    /// </summary>
    public string IntervalUnit { get; set; } = string.Empty;

    public string? ProductFamilyHandle { get; set; }
    public int ProductFamilyId { get; set; }
    public bool RequiresPaymentMethod { get; set; }
    public bool Archived { get; set; }
}
