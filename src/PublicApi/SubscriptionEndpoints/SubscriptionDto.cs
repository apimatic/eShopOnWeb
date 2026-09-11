namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string CurrentPeriodEndsAt { get; set; } = string.Empty;
    public string NextAssessmentAt { get; set; } = string.Empty;
    public string ActivatedAt { get; set; } = string.Empty;
}
