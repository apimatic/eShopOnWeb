namespace Microsoft.eShopWeb.Web.ViewModels.Subscriptions;

public class PlanViewModel
{
    public long Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool RequiresPaymentMethod { get; set; }
}
