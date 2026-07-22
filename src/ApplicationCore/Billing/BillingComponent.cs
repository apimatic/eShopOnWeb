namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A usage-billed component available to subscriptions on a product family.
/// </summary>
public class BillingComponent
{
    public int Id { get; set; }

    public string Handle { get; set; } = string.Empty;

    public string? Name { get; set; }

    /// <summary>
    /// The provider's component kind, verbatim (e.g. <c>metered_component</c>).
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>
    /// True only when the component is metered and can therefore accept usage records.
    /// </summary>
    public bool IsMetered { get; set; }

    /// <summary>
    /// Price per unit in major currency units (e.g. 0.01 for one cent).
    /// </summary>
    public decimal UnitPrice { get; set; }

    public string? PricingScheme { get; set; }

    public string? UnitName { get; set; }
}
