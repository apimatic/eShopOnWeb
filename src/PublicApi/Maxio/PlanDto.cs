namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class PlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal PriceInCents { get; set; }
    public string Interval { get; set; } = string.Empty;
    public string IntervalUnit { get; set; } = string.Empty;
}
