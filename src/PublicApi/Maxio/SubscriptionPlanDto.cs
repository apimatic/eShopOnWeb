namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// A subscribable plan, projected from a Maxio Product for eShopOnWeb API consumers.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public string PriceFormatted => $"${PriceInCents / 100m:0.00}";
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequireCreditCard { get; set; }
}
