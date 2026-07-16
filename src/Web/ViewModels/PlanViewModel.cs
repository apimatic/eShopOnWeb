namespace Microsoft.eShopWeb.Web.ViewModels;

public class PlanViewModel
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price => PriceInCents / 100m;
    public long PriceInCents { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
