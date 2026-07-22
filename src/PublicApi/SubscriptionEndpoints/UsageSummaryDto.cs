using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// What a subscription has consumed so far in the current billing period.
/// </summary>
public class UsageSummaryDto
{
    public int SubscriptionId { get; set; }
    public string ComponentHandle { get; set; }
    public string? UnitName { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? PeriodToDateQuantity { get; set; }
    public int? CurrentUnitBalance { get; set; }
    public decimal? EstimatedCharge { get; set; }
    public DateTimeOffset? PeriodStartedAt { get; set; }
    public DateTimeOffset? PeriodEndsAt { get; set; }
}
