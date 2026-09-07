using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string BillingCycle { get; set; } = "monthly";
}

public class SubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
