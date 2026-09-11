using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateResponse
{
    public Guid CorrelationId { get; set; }
    public int SubscriptionId { get; set; }
    public string State { get; set; } = "";
    public string PlanHandle { get; set; } = "";
    public decimal PriceInCents { get; set; }
    public string PriceFormatted => $"{PriceInCents / 100:C}";
    public DateTime? NextBillingAt { get; set; }
    public string CustomerReference { get; set; } = "";
}
