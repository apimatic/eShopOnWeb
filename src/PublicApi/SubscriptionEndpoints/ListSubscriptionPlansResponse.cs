namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; }
}

public class ListSubscriptionPlansResponse
{
    public System.Collections.Generic.List<SubscriptionPlanDto> Plans { get; set; } = new();
}
