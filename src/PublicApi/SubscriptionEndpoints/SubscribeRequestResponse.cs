namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class SubscribeResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int? SubscriptionId { get; set; }
    public string? State { get; set; }
    public string? NextBillingAt { get; set; }
    public string? PlanHandle { get; set; }
    public decimal? Price { get; set; }
}
