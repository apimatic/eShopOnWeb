namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// An add-on offered alongside the plans, such as a metered usage component.
/// </summary>
public class PlanComponentDto
{
    public string? Handle { get; set; }

    public string? Name { get; set; }

    public string? Kind { get; set; }

    public string? UnitName { get; set; }

    public long? PricePerUnitInCents { get; set; }

    /// <summary>Unit price as the billing system renders it, for example <c>0.01</c>.</summary>
    public string? UnitPrice { get; set; }

    public decimal? PricePerUnit { get; set; }

    public string? PricePerUnitDisplay { get; set; }

    public string? PricingScheme { get; set; }

    public bool? Recurring { get; set; }
}
