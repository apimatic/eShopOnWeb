namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// The running period-to-date balance of a metered component on a subscription.
/// </summary>
public class BillingUsageTotal
{
    public int SubscriptionId { get; set; }
    public int ComponentId { get; set; }
    public string? ComponentHandle { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// The accumulated, not-yet-invoiced number of units for the current period.
    /// </summary>
    public decimal UnitBalance { get; set; }

    /// <summary>
    /// The price charged per unit in the site currency (e.g. 0.01), not in minor units.
    /// </summary>
    public decimal? UnitPrice { get; set; }
}
