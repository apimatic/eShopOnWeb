namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int IntervalCount { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool HasTrial { get; set; }
    public bool RequiresPaymentMethod { get; set; }
    public bool Taxable { get; set; }
    public bool ExpiresNever { get; set; }
}
