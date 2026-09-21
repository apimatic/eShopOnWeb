using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API representation of a shopper's subscription as reflected by the billing system of record.</summary>
public class SubscriptionDto
{
    public int? SubscriptionId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public string? State { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
