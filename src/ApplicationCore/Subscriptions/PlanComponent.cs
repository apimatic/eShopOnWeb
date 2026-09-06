namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// An add-on component (metered, quantity-based, …) offered alongside the plans of a product family.
/// </summary>
public class PlanComponent
{
    public string? Handle { get; set; }

    public string? Name { get; set; }

    /// <summary>Component kind as reported by the billing system (for example <c>metered_component</c>).</summary>
    public string? Kind { get; set; }

    /// <summary>What one unit is called (for example <c>api call</c>).</summary>
    public string? UnitName { get; set; }

    public long? PricePerUnitInCents { get; set; }

    /// <summary>
    /// Unit price as the billing system renders it. Reported as a string rather than in cents, and it
    /// is the field that is actually populated for a metered component.
    /// </summary>
    public string? UnitPrice { get; set; }

    public string? PricingScheme { get; set; }

    public bool? Recurring { get; set; }
}
