using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest
{
    public string PlanHandle { get; set; } = string.Empty;
    public string? Email { get; set; }
}

public class CreateSubscriptionResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int? SubscriptionId { get; set; }
    public string? State { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public string? PlanName { get; set; }
    public decimal? PlanPrice { get; set; }
}
