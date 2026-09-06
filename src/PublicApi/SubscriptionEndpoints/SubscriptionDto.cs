using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public record SubscriptionDto
{
    public int? Id { get; set; }
    public string? State { get; set; }
    public long? PriceInCents { get; set; }
    public string? PlanName { get; set; }
    public string? PlanHandle { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}
