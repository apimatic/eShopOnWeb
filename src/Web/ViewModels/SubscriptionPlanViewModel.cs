namespace Microsoft.eShopWeb.Web.ViewModels;

public class SubscriptionPlanViewModel
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    public string DisplayInterval => Interval == 1 ? $"/ {IntervalUnit}" : $"every {Interval} {IntervalUnit}s";
}
