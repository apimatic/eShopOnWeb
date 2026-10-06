namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A billable component (e.g. a metered add-on) available on a subscription plan.
/// </summary>
public class SubscriptionComponentDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? UnitName { get; set; }
    public decimal? UnitPrice { get; set; }
    public int? PricePerUnitInCents { get; set; }
}
