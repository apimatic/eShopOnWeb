namespace Microsoft.eShopWeb.Web.ViewModels;

public class SubscriptionViewModel
{
    public int Id { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public string? PendingPlanHandle { get; set; }
    public int? UsageBalance { get; set; }
}
