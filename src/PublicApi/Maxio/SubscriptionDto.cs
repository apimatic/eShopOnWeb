using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class SubscriptionDto
{
    public int? Id { get; set; }

    public string? State { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public decimal? Price { get; set; }

    public string? Currency { get; set; }

    public DateTimeOffset? NextBillingDate { get; set; }
}
