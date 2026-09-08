using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription belonging to the caller.
/// </summary>
public class SubscriptionSummaryDto
{
    public int? SubscriptionId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public decimal? PriceAmount { get; set; }
    public string? Currency { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}
