using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class SubscriptionDto
{
    public int Id { get; set; }

    public string Reference { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public string PricePointName { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public string Currency { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public DateTimeOffset? NextBillingDate { get; set; }
}
