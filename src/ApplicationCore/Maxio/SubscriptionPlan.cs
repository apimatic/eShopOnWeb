namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A plan (Maxio product) available for subscription under the configured product family.
/// </summary>
public class SubscriptionPlan
{
    public int? Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public long? PriceInCents { get; set; }
    public string? Currency { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}
