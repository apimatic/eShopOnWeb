namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// A subscribable plan, as presented to shoppers.
/// </summary>
public sealed class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public string? Currency { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? ProductFamilyHandle { get; set; }
}
