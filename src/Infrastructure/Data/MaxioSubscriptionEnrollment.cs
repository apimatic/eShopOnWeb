namespace Microsoft.eShopWeb.Infrastructure.Data;

public class MaxioSubscriptionEnrollment
{
    public string Key { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public int? SubscriptionId { get; set; }
    public string Status { get; set; } = "Pending";
}
